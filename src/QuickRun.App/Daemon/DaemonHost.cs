using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuickRun.App.Commands;
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
        builder.Services.AddSingleton(new RunRegistry(store));
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

        app.MapPost("/api/dashboard/runs/{id}/stop", (string id, HttpContext context, Dashboard dashboard, RunRegistry runs) =>
            !DashboardAuthorized(context, dashboard) ? Forbidden()
                : runs.Stop(id) ? Results.NoContent() : Results.NotFound());

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

    private static bool DashboardAuthorized(HttpContext context, Dashboard dashboard) =>
        dashboard.Authorized(context.Request.Headers[Dashboard.TokenHeader].ToString());

    private static IResult Forbidden() =>
        Results.Json(new { error = "reload the dashboard" }, Json, statusCode: StatusCodes.Status403Forbidden);

    /// <summary>Shared by the extension's stream and the dashboard's.</summary>
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
