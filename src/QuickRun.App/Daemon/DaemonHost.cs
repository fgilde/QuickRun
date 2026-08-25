using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuickRun.App.Commands;
using QuickRun.Core.Config;
using QuickRun.Core.Git;
using QuickRun.Core.Inputs;
using QuickRun.Core;
using QuickRun.Core.Update;
using QuickRun.Core.Workspace;

namespace QuickRun.App.Daemon;

public sealed record RunRequest(
    string? Repo,
    string? Ref,
    int? Pr,
    string? Subdir,
    string? Config,
    Dictionary<string, string?>? Inputs);

/// <summary>Values for the inputs a config declares.</summary>
public sealed record InputsRequest(Dictionary<string, string?>? Inputs);

/// <summary>A config being edited, checked without running anything.</summary>
public sealed record ConfigTextRequest(string? Text);

/// <param name="Action">
/// Omitted or unknown reads the override, <c>save</c> writes it, <c>delete</c> removes it. One
/// endpoint because the local UI does all three from the same panel.
/// </param>
public sealed record ConfigOverrideRequest(string? Repo, string? Text, string? Action);

/// <summary>What the local UI asks for to fill its branch picker.</summary>
public sealed record BranchRequest(string? Repo, string? Token);

/// <summary>
/// A run started from the local UI. Unlike the extension's request this one may carry a token: the
/// page asking is QuickRun's own, and a private repository has to come from somewhere.
/// </summary>
public sealed record DashboardRunRequest(
    string? Repo,
    string? Ref,
    int? Pr,
    string? Subdir,
    string? Config,
    Dictionary<string, string?>? Inputs,
    string? Token,
    bool? Fresh,
    /// <summary>A config from the builder, tested before it is saved anywhere.</summary>
    string? ConfigText = null);

/// <summary>
/// The localhost listener the browser extension talks to. Loopback only, and every endpoint that
/// can start something requires the request to come from a browser extension.
/// </summary>
public static class DaemonHost
{
    public const int DefaultPort = 9876;

    /// <summary>
    /// The origins allowed to drive a run.
    /// <para>
    /// A browser sets Origin itself on every cross-origin request and a page cannot forge it, so
    /// this is the one claim about the caller that can be trusted. Extension origins only:
    /// https://github.com is deliberately absent, because allowing it would let any script running
    /// on that site start runs on this machine.
    /// </para>
    /// </summary>
    private static readonly string[] ExtensionOrigins =
    {
        "chrome-extension://",
        "moz-extension://",
        "safari-web-extension://",
    };

    /// <summary>
    /// Enums go over the wire as names: the extension should read "awaitingConfirmation", not 0.
    /// </summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static WebApplication Build(int port, WorkspaceStore store)
    {
        var builder = WebApplication.CreateSlimBuilder();

        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        // Loopback, never 0.0.0.0: nothing outside this machine may reach the run endpoint.
        builder.Services.Configure<KestrelServerOptions>(options => options.ListenLocalhost(port));

        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton(new RunRegistry(store, UiCommand.Launch));
        builder.Services.AddSingleton(new Dashboard());
        builder.Services.AddSingleton(new ListenerPort(port));

        var app = builder.Build();

        app.Use(async (context, next) =>
        {
            ApplyCors(context);

            // Chromium sends an OPTIONS preflight with Access-Control-Request-Private-Network
            // for https://github.com -> http://127.0.0.1; without the matching header the real
            // request never arrives.
            if (HttpMethods.IsOptions(context.Request.Method))
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            await next();
        });

        MapEndpoints(app);
        MapDashboard(app);
        return app;
    }

    /// <summary>
    /// The local dashboard: what a user sees when they double-click the binary or open the tray
    /// icon. Its endpoints are guarded by the page's own token rather than the extension's, because
    /// CORS stops another origin reading a response but not sending a request.
    /// </summary>
    private static void MapDashboard(WebApplication app)
    {
        app.MapGet("/", (Dashboard dashboard, ListenerPort port) =>
            Results.Content(dashboard.Render(port.Value), "text/html; charset=utf-8"));

        app.MapGet("/icon.png", () =>
        {
            var stream = typeof(Dashboard).Assembly
                .GetManifestResourceStream("QuickRun.App.Daemon.icon.png");
            return stream is null ? Results.NotFound() : Results.Stream(stream, "image/png");
        });

        // The editor and the schema. No token: these are the same bytes for everyone and reveal
        // nothing about this machine, and the editor loads dozens of chunks lazily.
        app.MapGet("/monaco/{**path}", (string path) => Asset($"monaco/{path}"));

        app.MapGet("/quickrun.schema.json", () => Asset("quickrun.schema.json"));

        app.MapGet("/api/dashboard/state", (HttpContext context, Dashboard dashboard,
            RunRegistry runs, WorkspaceStore store) =>
        {
            if (!DashboardAuthorized(context, dashboard)) return Forbidden();

            return Results.Json(new
            {
                version = BuildInfo.Version,
                installSource = InstallSources.DetectCurrent(store.Root).ToString().ToLowerInvariant(),
                workspaceRoot = store.Root,
                runs = runs.All(),
            }, Json);
        });

        // Separate from the state poll on purpose: listing workspaces sums the size of every file
        // in every checkout, which is far too much disk to touch every few seconds - and the page
        // only needs it when someone is looking at that tab.
        app.MapGet("/api/dashboard/workspaces", (HttpContext context, Dashboard dashboard, WorkspaceStore store) =>
        {
            if (!DashboardAuthorized(context, dashboard)) return Forbidden();

            return Results.Json(new
            {
                workspaceRoot = store.Root,
                workspaces = store.List().Select(w => new
                {
                    w.Id,
                    w.Repo,
                    w.Ref,
                    size = Output.Size(w.Bytes),
                    w.LastUsed,
                    w.LastCommit,
                    w.LastOk,
                }),
            }, Json);
        });


        app.MapGet("/api/dashboard/update", async (HttpContext context, Dashboard dashboard, WorkspaceStore store) =>
        {
            if (!DashboardAuthorized(context, dashboard)) return Forbidden();

            var source = InstallSources.DetectCurrent(store.Root);
            return Results.Json(await new UpdateChecker().CheckAsync(BuildInfo.Version, source), Json);
        });

        // ---- the config builder ---------------------------------------------------------------

        // Checks a config without running it, with the same parser and validator a run uses. A
        // JSON-schema approximation in the browser would disagree with the real thing sooner or later.
        app.MapPost("/api/dashboard/config/check", (ConfigTextRequest request, HttpContext context, Dashboard dashboard) =>
        {
            if (!DashboardAuthorized(context, dashboard)) return Forbidden();

            try
            {
                var config = ConfigParser.Parse(request.Text ?? "", OSKinds.Current);
                var issues = ConfigValidator.Validate(config);

                return Results.Json(new
                {
                    ok = !issues.Any(i => i.IsError),
                    name = config.Name,
                    tasks = config.Tasks.Select(t => t.Name),
                    issues = issues.Select(i => new { i.Path, i.Message, i.IsError }),
                }, Json);
            }
            catch (ConfigException e)
            {
                return Results.Json(new
                {
                    ok = false,
                    issues = new[] { new { Path = "", Message = e.Message, IsError = true } },
                }, Json);
            }
        });

        // The config this repository would run with, as a starting point for editing: your own
        // override, else what it ships, else what its foreign scripts or the detector give.
        app.MapPost("/api/dashboard/config/suggest", async (DashboardRunRequest request, HttpContext context,
            Dashboard dashboard, WorkspaceStore store) =>
        {
            if (!DashboardAuthorized(context, dashboard)) return Forbidden();
            if (string.IsNullOrWhiteSpace(request.Repo)) return Results.BadRequest(new { error = "repo is required" });

            var suggestion = await Task.Run(() => ConfigSuggestion.For(
                request.Repo!.Trim(),
                string.IsNullOrWhiteSpace(request.Ref) ? null : request.Ref!.Trim(),
                string.IsNullOrWhiteSpace(request.Token) ? null : request.Token,
                store));

            return suggestion.Error is { } error
                ? Results.Json(new { error }, Json, statusCode: StatusCodes.Status422UnprocessableEntity)
                : Results.Json(suggestion, Json);
        });

        // Your own config for a repository, kept in QuickRun's own directory rather than in the
        // checkout: --fresh deletes that, and it is not your repository to leave files in.
        app.MapPost("/api/dashboard/config/mine", (ConfigOverrideRequest request, HttpContext context,
            Dashboard dashboard, WorkspaceStore store) =>
        {
            if (!DashboardAuthorized(context, dashboard)) return Forbidden();
            if (string.IsNullOrWhiteSpace(request.Repo)) return Results.BadRequest(new { error = "repo is required" });

            string repo;
            try { repo = RunPipeline.Normalize(request.Repo!.Trim()); }
            catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }

            var overrides = new ConfigOverrides(store.Root);

            switch (request.Action?.ToLowerInvariant())
            {
                case "save":
                    if (string.IsNullOrWhiteSpace(request.Text))
                        return Results.BadRequest(new { error = "there is nothing to save" });

                    try
                    {
                        var parsed = ConfigParser.Parse(request.Text!, OSKinds.Current);
                        var errors = ConfigValidator.Validate(parsed).Where(i => i.IsError).ToList();
                        if (errors.Count > 0)
                            return Results.Json(new { error = string.Join("; ", errors.Select(i => i.Message)) },
                                Json, statusCode: StatusCodes.Status422UnprocessableEntity);
                    }
                    catch (ConfigException e)
                    {
                        return Results.Json(new { error = e.Message }, Json,
                            statusCode: StatusCodes.Status422UnprocessableEntity);
                    }

                    overrides.Remember(repo);
                    return Results.Json(new { repo, path = overrides.Write(repo, request.Text!) }, Json);

                case "delete":
                    return overrides.Delete(repo)
                        ? Results.Json(new { repo, deleted = true }, Json)
                        : Results.NotFound();

                default:
                    return Results.Json(new
                    {
                        repo,
                        path = overrides.PathFor(repo),
                        has = overrides.Has(repo),
                        text = overrides.Read(repo),
                    }, Json);
            }
        });

        app.MapGet("/api/dashboard/configs", (HttpContext context, Dashboard dashboard, WorkspaceStore store) =>
            !DashboardAuthorized(context, dashboard)
                ? Forbidden()
                : Results.Json(new ConfigOverrides(store.Root).List()
                    .Select(o => new { repo = o.Repo, path = o.Path, changed = o.Changed }), Json));

        // Whether quickrun:// reaches this build. A handler registered to a binary that has since
        // moved looks installed and does nothing, which is the failure this exists to show.
        app.MapGet("/api/dashboard/integration", (HttpContext context, Dashboard dashboard) =>
        {
            if (!DashboardAuthorized(context, dashboard)) return Forbidden();

            var status = SystemIntegration.Status();
            return Results.Json(new
            {
                status.Registered,
                status.Command,
                status.Stale,
                status.Detail,
                executable = Environment.ProcessPath,
            }, Json);
        });

        app.MapPost("/api/dashboard/integration/register", (HttpContext context, Dashboard dashboard) =>
        {
            if (!DashboardAuthorized(context, dashboard)) return Forbidden();

            var executable = Environment.ProcessPath;
            if (executable is null)
                return Results.Json(new { error = "cannot determine the running executable" },
                    Json, statusCode: StatusCodes.Status500InternalServerError);

            var step = SystemIntegration.RegisterScheme(executable);
            var status = SystemIntegration.Status();

            return Results.Json(new
            {
                ok = step.Ok,
                detail = step.Detail,
                status.Registered,
                status.Stale,
            }, Json);
        });

        // What refs this repository has, and which of them this user has run before. A POST because
        // the token belongs in a body rather than in a URL that ends up in logs and history.
        app.MapPost("/api/dashboard/branches", (BranchRequest request, HttpContext context,
            Dashboard dashboard, WorkspaceStore store) =>
        {
            if (!DashboardAuthorized(context, dashboard)) return Forbidden();
            if (string.IsNullOrWhiteSpace(request.Repo)) return Results.BadRequest(new { error = "repo is required" });

            string repo;
            try { repo = RunPipeline.Normalize(request.Repo!.Trim()); }
            catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }

            var recent = RefSuggestions.Recent(store.List(), repo);
            var (branches, error) = new GitClient(new CredentialResolver(request.Token)).ListBranches(repo);

            // A repository nobody can list is still runnable by name, so the recent refs and a
            // typed-in branch stay available: this is a suggestion, not a gate.
            return Results.Json(new
            {
                repo,
                branches = branches ?? Array.Empty<string>(),
                recent,
                @default = RefSuggestions.Default(branches ?? Array.Empty<string>(), recent),
                error = branches is null ? error ?? "the branches could not be listed" : null,
            }, Json);
        });

        // Prepares a run the local UI asked for. Like the extension's endpoint it executes nothing:
        // the page shows the command list and confirms separately.
        app.MapPost("/api/dashboard/run", async (DashboardRunRequest request, HttpContext context,
            Dashboard dashboard, RunRegistry runs) =>
        {
            if (!DashboardAuthorized(context, dashboard)) return Forbidden();
            if (string.IsNullOrWhiteSpace(request.Repo)) return Results.BadRequest(new { error = "repo is required" });

            var args = new RunArgs(
                request.Repo!.Trim(),
                string.IsNullOrWhiteSpace(request.Ref) ? null : request.Ref!.Trim(),
                request.Pr,
                string.IsNullOrWhiteSpace(request.Subdir) ? null : request.Subdir!.Trim(),
                (request.Inputs ?? new()).Select(kv => $"{kv.Key}={kv.Value}").ToList(),
                string.IsNullOrWhiteSpace(request.Token) ? null : request.Token,
                Fresh: request.Fresh ?? false,
                Yes: true,
                NoOpen: true,
                ConfigPath: string.IsNullOrWhiteSpace(request.Config) ? null : request.Config,
                ConfigText: string.IsNullOrWhiteSpace(request.ConfigText) ? null : request.ConfigText);

            var (summary, error) = await runs.PrepareAsync(args);

            return error is null
                ? Results.Json(summary, Json)
                : Results.Json(new { error, run = summary }, Json, statusCode: StatusCodes.Status422UnprocessableEntity);
        });

        app.MapPost("/api/dashboard/runs/{id}/inputs", async (string id, InputsRequest request,
            HttpContext context, Dashboard dashboard, RunRegistry runs) =>
        {
            if (!DashboardAuthorized(context, dashboard)) return Forbidden();

            var (summary, error) = await runs.SupplyInputsAsync(id, request.Inputs ?? new());

            if (summary is null) return Results.NotFound(new { error });

            return error is null
                ? Results.Json(summary, Json)
                : Results.Json(new { error, run = summary }, Json, statusCode: StatusCodes.Status422UnprocessableEntity);
        });

        app.MapPost("/api/dashboard/runs/{id}/confirm", (string id, HttpContext context, Dashboard dashboard, RunRegistry runs) =>
        {
            if (!DashboardAuthorized(context, dashboard)) return Forbidden();

            return runs.Confirm(id)
                ? Results.Json(runs.Get(id), Json)
                : Results.Json(new { error = "unknown run, or it has already started" },
                    Json, statusCode: StatusCodes.Status409Conflict);
        });

        // Off the list, but nothing is deleted: the workspace and its checkout stay where they are.
        app.MapPost("/api/dashboard/runs/{id}/forget", (string id, HttpContext context, Dashboard dashboard, RunRegistry runs) =>
            !DashboardAuthorized(context, dashboard) ? Forbidden()
                : runs.Forget(id) ? Results.NoContent()
                    : Results.Json(new { error = "a run that is still going cannot be removed" },
                        Json, statusCode: StatusCodes.Status409Conflict));

        app.MapPost("/api/dashboard/runs/{id}/cancel", (string id, HttpContext context, Dashboard dashboard, RunRegistry runs) =>
            !DashboardAuthorized(context, dashboard) ? Forbidden()
                : runs.Cancel(id) ? Results.NoContent() : Results.NotFound());

        app.MapPost("/api/dashboard/runs/{id}/stop", (string id, HttpContext context, Dashboard dashboard, RunRegistry runs) =>
            !DashboardAuthorized(context, dashboard) ? Forbidden()
                : runs.Stop(id) ? Results.NoContent() : Results.NotFound());

        app.MapPost("/api/dashboard/runs/{id}/reveal", (string id, HttpContext context, Dashboard dashboard, RunRegistry runs) =>
        {
            if (!DashboardAuthorized(context, dashboard)) return Forbidden();
            return Reveal(runs, id);
        });

        app.MapDelete("/api/dashboard/workspaces/{id}", (string id, HttpContext context, Dashboard dashboard, WorkspaceStore store) =>
        {
            if (!DashboardAuthorized(context, dashboard)) return Forbidden();

            try { return store.Remove(id) ? Results.NoContent() : Results.NotFound(); }
            catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
        });

        app.MapDelete("/api/dashboard/workspaces", (HttpContext context, Dashboard dashboard, WorkspaceStore store) =>
            !DashboardAuthorized(context, dashboard) ? Forbidden()
                : Results.Json(new { removed = store.RemoveAll() }, Json));

        // EventSource cannot set headers, so the dashboard's stream takes its token in the query
        // string. Same-origin only, and the token is not a bearer credential for anything else.
        app.MapGet("/api/dashboard/runs/{id}/events", async (string id, string? token,
            HttpContext context, Dashboard dashboard, RunRegistry runs) =>
        {
            if (!dashboard.Authorized(token))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await StreamEventsAsync(context, runs, id);
        });
    }

    /// <summary>An embedded asset, cached hard: a vendored editor never changes under one build.</summary>
    private static IResult Asset(string path)
    {
        if (StaticAssets.Open(path) is not { } asset) return Results.NotFound();

        return Results.Stream(asset.Content, asset.ContentType, enableRangeProcessing: false);
    }

    private static bool DashboardAuthorized(HttpContext context, Dashboard dashboard) =>
        dashboard.Authorized(context.Request.Headers[Dashboard.TokenHeader].ToString());

    private static IResult Forbidden() =>
        Results.Json(new { error = "reload the dashboard" }, Json, statusCode: StatusCodes.Status403Forbidden);

    /// <summary>Shared by the extension's stream and the dashboard's.</summary>
    /// <summary>
    /// Opens a run's workspace in the file manager. The path is read from the registry, never from
    /// the request: this must not become a way to make QuickRun open an arbitrary folder.
    /// </summary>
    private static IResult Reveal(RunRegistry runs, string id)
    {
        var workspace = runs.Get(id)?.Workspace;
        if (workspace is null || !Directory.Exists(workspace)) return Results.NotFound();

        UiCommand.Launch(workspace);
        return Results.Ok();
    }

    private static async Task StreamEventsAsync(HttpContext context, RunRegistry runs, string id)
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers["X-Accel-Buffering"] = "no";

        await foreach (var e in runs.Subscribe(id, context.RequestAborted))
        {
            await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(e, Json)}\n\n",
                context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
        }
    }

    /// <summary>So the dashboard page can show where it is listening.</summary>
    public sealed record ListenerPort(int Value);

    private static void ApplyCors(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (FromExtensionOrigin(origin))
            context.Response.Headers.AccessControlAllowOrigin = origin;

        context.Response.Headers.AccessControlAllowHeaders = "Content-Type";
        context.Response.Headers.AccessControlAllowMethods = "GET, POST, OPTIONS";
        context.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
        context.Response.Headers.AccessControlMaxAge = "600";
    }

    private static void MapEndpoints(WebApplication app)
    {
        // No token: telling any page that QuickRun exists is the entire point, and this is the
        // only thing it reveals - no repository names, no paths, no run contents.
        app.MapGet("/api/ping", (RunRegistry runs) => Results.Json(new
        {
            product = "QuickRun",
            version = BuildInfo.Version,
            busy = runs.AnyActive,
        }, Json));

        app.MapPost("/api/run", async (RunRequest request, HttpContext context, RunRegistry runs) =>
        {
            if (!Authorized(context)) return Unauthorized();
            if (string.IsNullOrWhiteSpace(request.Repo)) return Results.BadRequest(new { error = "repo is required" });

            var args = new RunArgs(
                request.Repo,
                request.Ref,
                request.Pr,
                request.Subdir,
                (request.Inputs ?? new()).Select(kv => $"{kv.Key}={kv.Value}").ToList(),
                Token: null,
                Fresh: false,
                Yes: true,
                NoOpen: true,
                ConfigPath: request.Config);

            var (summary, error) = await runs.PrepareAsync(args);

            // Nothing has executed yet. The caller must confirm the command list first.
            return error is null
                ? Results.Json(summary, Json)
                : Results.Json(new { error, run = summary }, Json, statusCode: StatusCodes.Status422UnprocessableEntity);
        });

        // The values for a config's inputs. The plan is rebuilt with them, so the command list the
        // user approves is the one those values produced.
        app.MapPost("/api/runs/{id}/inputs", async (string id, InputsRequest request,
            HttpContext context, RunRegistry runs) =>
        {
            if (!Authorized(context)) return Unauthorized();

            var (summary, error) = await runs.SupplyInputsAsync(id, request.Inputs ?? new());

            if (summary is null) return Results.NotFound(new { error });

            return error is null
                ? Results.Json(summary, Json)
                : Results.Json(new { error, run = summary }, Json, statusCode: StatusCodes.Status422UnprocessableEntity);
        });

        app.MapPost("/api/runs/{id}/confirm", (string id, HttpContext context, RunRegistry runs) =>
        {
            if (!Authorized(context)) return Unauthorized();

            return runs.Confirm(id)
                ? Results.Json(runs.Get(id), Json)
                : Results.Json(new { error = "unknown run, or it has already started" },
                    Json, statusCode: StatusCodes.Status409Conflict);
        });

        app.MapPost("/api/runs/{id}/stop", (string id, HttpContext context, RunRegistry runs) =>
        {
            if (!Authorized(context)) return Unauthorized();
            return runs.Stop(id) ? Results.Ok() : Results.NotFound();
        });

        // Opens the run's workspace in the file manager. The path comes from the registry, never
        // from the request: this must not become a way to make QuickRun open an arbitrary folder.
        app.MapPost("/api/runs/{id}/reveal", (string id, HttpContext context, RunRegistry runs) =>
        {
            if (!Authorized(context)) return Unauthorized();

            return Reveal(runs, id);
        });

        app.MapGet("/api/runs/{id}", (string id, HttpContext context, RunRegistry runs) =>
        {
            if (!Authorized(context)) return Unauthorized();
            return runs.Get(id) is { } summary ? Results.Json(summary, Json) : Results.NotFound();
        });

        app.MapGet("/api/runs/{id}/events", async (string id, HttpContext context, RunRegistry runs) =>
        {
            if (!Authorized(context))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await StreamEventsAsync(context, runs, id);
        });

        app.MapGet("/api/update", async (HttpContext context, WorkspaceStore store) =>
        {
            if (!Authorized(context)) return Unauthorized();

            var source = InstallSources.DetectCurrent(store.Root);
            var status = await new UpdateChecker().CheckAsync(BuildInfo.Version, source);
            return Results.Json(status, Json);
        });
    }

    /// <summary>
    /// Whether this request may start something.
    /// <para>
    /// An absent Origin means the caller is not a browser - a local program such as curl or
    /// QuickRun's own CLI. That is allowed: a program already running on this machine has the
    /// user's privileges and gains nothing by going through the daemon. What must not be allowed
    /// is a web page, and a web page always sends its Origin.
    /// </para>
    /// </summary>
    internal static bool Authorized(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        return string.IsNullOrEmpty(origin) || FromExtensionOrigin(origin);
    }

    private static bool FromExtensionOrigin(string origin) =>
        !string.IsNullOrEmpty(origin)
        && ExtensionOrigins.Any(scheme => origin.StartsWith(scheme, StringComparison.Ordinal));

    private static IResult Unauthorized() =>
        Results.Json(new { error = "only a browser extension may start a run" },
            Json, statusCode: StatusCodes.Status403Forbidden);
}
