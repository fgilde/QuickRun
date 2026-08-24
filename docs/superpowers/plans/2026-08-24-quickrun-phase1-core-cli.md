# QuickRun Phase 1 (Core + CLI) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A cross-platform `quickrun` CLI that checks a git repository out into a managed workspace, reads `quickrun.yml` (or detects a fallback entry point), collects and validates user inputs, checks prerequisites, shows the exact commands for confirmation, and supervises the resulting processes.

**Architecture:** All logic lives in `QuickRun.Core`, a plain library with no UI dependency, so that Phase 2's Blazor UI and Phase 4's WASM playground consume the same parser, validator and runner. `QuickRun.App` is a thin Spectre.Console.Cli shell over Core. Configs are parsed into a canonical record model — shorthand is expanded during parsing so the runner sees exactly one shape.

**Tech Stack:** .NET 10, YamlDotNet (YAML), Spectre.Console.Cli (commands, prompts, tables), xUnit (tests). No other dependencies.

**Spec:** `docs/superpowers/specs/2026-08-24-quickrun-design.md`

## Global Constraints

- Target framework `net10.0`. `Nullable` and `ImplicitUsings` enabled solution-wide via `Directory.Build.props`.
- Central package management: all versions in `Directory.Packages.props`. Add packages with `dotnet add package <name>` so versions pin to whatever is current; never hand-write a version guess.
- `QuickRun.Core` must not reference any UI, ASP.NET or CLI package. It is consumed by a Blazor WASM project in Phase 4, so no `System.Diagnostics.Process` calls at type-initialisation time and no file-system access in constructors.
- Every external process runs with `GIT_TERMINAL_PROMPT=0` and no shell window (`CreateNoWindow = true`, `UseShellExecute = false`).
- Tokens must never appear in returned strings, logs, or exception messages. Every path that can carry one goes through `GitClient.Scrub`.
- Nothing executes before the user has seen the command list. The CLI prints the plan and requires confirmation unless `--yes` is passed.
- Workspaces live under `QuickRun/runs` in the OS application-data directory, never in `%TEMP%`. `QUICKRUN_HOME` overrides the root (used by tests).
- Platform keys in configs are exactly `windows`, `linux`, `macos`.
- All user-facing CLI text is English.

---

### Task 1: Solution scaffold and version-range comparison

**Files:**
- Create: `QuickRun.sln`, `Directory.Build.props`, `Directory.Packages.props`, `.gitignore`
- Create: `src/QuickRun.Core/QuickRun.Core.csproj`
- Create: `src/QuickRun.Core/Requires/VersionCheck.cs`
- Test: `tests/QuickRun.Core.Tests/QuickRun.Core.Tests.csproj`, `tests/QuickRun.Core.Tests/VersionCheckTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `static bool QuickRun.Core.Requires.VersionCheck.Satisfies(string? found, string? range)` — `range` null/empty returns true; `found` null returns false. Also `static string? VersionCheck.Extract(string text)` pulling the first dotted version out of arbitrary tool output.

- [ ] **Step 1: Create the solution skeleton**

```bash
cd C:/dev/privat/github/QuickRun
dotnet new sln -n QuickRun
dotnet new classlib -o src/QuickRun.Core -n QuickRun.Core
dotnet new xunit  -o tests/QuickRun.Core.Tests -n QuickRun.Core.Tests
rm src/QuickRun.Core/Class1.cs tests/QuickRun.Core.Tests/UnitTest1.cs
dotnet sln add src/QuickRun.Core tests/QuickRun.Core.Tests
dotnet add tests/QuickRun.Core.Tests reference src/QuickRun.Core
```

- [ ] **Step 2: Add the shared build properties**

`Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
</Project>
```

`Directory.Packages.props`:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup />
</Project>
```

`.gitignore` — use the standard .NET set:

```bash
dotnet new gitignore
printf 'site/node_modules/\nsite/.vitepress/dist/\nsite/.vitepress/cache/\nextension/dist/\n' >> .gitignore
```

- [ ] **Step 3: Write the failing test**

`tests/QuickRun.Core.Tests/VersionCheckTests.cs`:

```csharp
using QuickRun.Core.Requires;

namespace QuickRun.Core.Tests;

public class VersionCheckTests
{
    [Theory]
    [InlineData("10.0.300", ">=9.0", true)]
    [InlineData("9.0.205", ">=9.0", true)]
    [InlineData("8.0.404", ">=9.0", false)]
    [InlineData("24.13.1", ">20", true)]
    [InlineData("20.0.0", ">20", false)]
    [InlineData("3.12.1", "<=3.12", false)]
    [InlineData("3.11.9", "<=3.12", true)]
    [InlineData("1.2.3", "=1.2.3", true)]
    [InlineData("1.2.4", "=1.2.3", false)]
    [InlineData("1.2.3", "1.2.3", true)]
    [InlineData("1.2.3", null, true)]
    [InlineData(null, ">=1.0", false)]
    public void Satisfies_compares_dotted_versions(string? found, string? range, bool expected)
        => Assert.Equal(expected, VersionCheck.Satisfies(found, range));

    [Theory]
    [InlineData("v24.13.1", "24.13.1")]
    [InlineData("Python 3.12.1", "3.12.1")]
    [InlineData("git version 2.51.2.windows.1", "2.51.2")]
    [InlineData("Docker version 27.3.1, build ce12230", "27.3.1")]
    [InlineData("no digits here", null)]
    public void Extract_pulls_the_first_dotted_version(string text, string? expected)
        => Assert.Equal(expected, VersionCheck.Extract(text));
}
```

Note on the `git version` case: `Extract` stops at the first non-numeric segment, so `2.51.2.windows.1` yields `2.51.2`. Rejoining across the word boundary would invent a version nobody reported.

- [ ] **Step 4: Run the test and watch it fail**

Run: `dotnet test tests/QuickRun.Core.Tests`
Expected: FAIL — `The type or namespace name 'VersionCheck' could not be found`.

- [ ] **Step 5: Implement `VersionCheck`**

`src/QuickRun.Core/Requires/VersionCheck.cs`:

```csharp
using System.Text.RegularExpressions;

namespace QuickRun.Core.Requires;

/// Compares tool versions against simple ranges (">=9.0", ">20", "<=3.12", "=1.2.3", "1.2.3").
/// Deliberately not a semver implementation: tool output is not semver, and ranges in
/// quickrun.yml are single comparisons.
public static partial class VersionCheck
{
    public static bool Satisfies(string? found, string? range)
    {
        if (string.IsNullOrWhiteSpace(range)) return true;
        if (string.IsNullOrWhiteSpace(found)) return false;

        var (op, wanted) = SplitRange(range.Trim());
        var a = Parse(found);
        var b = Parse(wanted);
        if (a is null || b is null) return false;

        var cmp = Compare(a, b, op == "=" ? b.Length : Math.Max(a.Length, b.Length));
        return op switch
        {
            ">=" => cmp >= 0,
            ">" => cmp > 0,
            "<=" => cmp <= 0,
            "<" => cmp < 0,
            _ => cmp == 0,
        };
    }

    public static string? Extract(string text)
    {
        var m = VersionPattern().Match(text ?? "");
        return m.Success ? m.Value : null;
    }

    private static (string op, string version) SplitRange(string range)
    {
        foreach (var op in new[] { ">=", "<=", ">", "<", "=" })
            if (range.StartsWith(op, StringComparison.Ordinal))
                return (op, range[op.Length..].Trim());
        return ("=", range);
    }

    private static int[]? Parse(string text)
    {
        var v = Extract(text);
        if (v is null) return null;
        return v.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
    }

    private static int Compare(int[] a, int[] b, int len)
    {
        for (var i = 0; i < len; i++)
        {
            var x = i < a.Length ? a[i] : 0;
            var y = i < b.Length ? b[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    [GeneratedRegex(@"\d+(\.\d+)*")]
    private static partial Regex VersionPattern();
}
```

- [ ] **Step 6: Run the tests and watch them pass**

Run: `dotnet test tests/QuickRun.Core.Tests`
Expected: PASS, 16 tests.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: solution scaffold and tool version range comparison"
```

---

### Task 2: Command runner and shell resolution

**Files:**
- Create: `src/QuickRun.Core/Process/ShellCommand.cs`
- Create: `src/QuickRun.Core/Process/CommandRunner.cs`
- Test: `tests/QuickRun.Core.Tests/ShellCommandTests.cs`, `tests/QuickRun.Core.Tests/CommandRunnerTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `enum QuickRun.Core.OSKind { Windows, Linux, MacOs }` and `static OSKind OSKinds.Current`
  - `static (string File, string[] Args) ShellCommand.Resolve(string command, OSKind os, Func<string,bool> fileExists)`
  - `sealed record CommandResult(int ExitCode, string Output, bool TimedOut)`
  - `sealed record ProcessSpec(string Command, string? Cwd, IReadOnlyDictionary<string,string>? Env)`
  - `static CommandResult CommandRunner.Capture(string file, IEnumerable<string> args, string? cwd = null, IReadOnlyDictionary<string,string>? env = null, int timeoutMs = 120_000)`
  - `static Task<int> CommandRunner.StreamAsync(ProcessSpec spec, Action<string,bool> onLine, CancellationToken ct)` — `onLine(text, isError)`; kills the whole process tree on cancellation.

- [ ] **Step 1: Write the failing shell-resolution test**

`tests/QuickRun.Core.Tests/ShellCommandTests.cs`:

```csharp
using QuickRun.Core;
using QuickRun.Core.Process;

namespace QuickRun.Core.Tests;

public class ShellCommandTests
{
    private static bool NoBash(string path) => false;
    private static bool GitBash(string path) => path.EndsWith("bash.exe", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Linux_uses_sh_dash_c()
    {
        var (file, args) = ShellCommand.Resolve("npm run dev", OSKind.Linux, NoBash);
        Assert.Equal("/bin/sh", file);
        Assert.Equal(new[] { "-c", "npm run dev" }, args);
    }

    [Fact]
    public void Windows_uses_cmd_slash_c()
    {
        var (file, args) = ShellCommand.Resolve("npm run dev", OSKind.Windows, NoBash);
        Assert.Equal("cmd.exe", file);
        Assert.Equal(new[] { "/c", "npm run dev" }, args);
    }

    [Fact]
    public void Windows_routes_sh_scripts_through_git_bash_when_present()
    {
        var (file, args) = ShellCommand.Resolve("./run.sh --fast", OSKind.Windows, GitBash);
        Assert.EndsWith("bash.exe", file);
        Assert.Equal(new[] { "-c", "./run.sh --fast" }, args);
    }

    [Fact]
    public void Windows_falls_back_to_cmd_when_git_bash_is_missing()
    {
        var (file, _) = ShellCommand.Resolve("./run.sh", OSKind.Windows, NoBash);
        Assert.Equal("cmd.exe", file);
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test tests/QuickRun.Core.Tests --filter ShellCommandTests`
Expected: FAIL — `ShellCommand` does not exist.

- [ ] **Step 3: Implement `OSKind` and `ShellCommand`**

`src/QuickRun.Core/Process/ShellCommand.cs`:

```csharp
using System.Runtime.InteropServices;

namespace QuickRun.Core;

public enum OSKind { Windows, Linux, MacOs }

public static class OSKinds
{
    public static OSKind Current =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? OSKind.Windows
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? OSKind.MacOs
        : OSKind.Linux;

    public static string Key(this OSKind os) => os switch
    {
        OSKind.Windows => "windows",
        OSKind.MacOs => "macos",
        _ => "linux",
    };
}

namespace QuickRun.Core.Process
{
    public static class ShellCommand
    {
        private static readonly string[] GitBashCandidates =
        {
            @"C:\Program Files\Git\bin\bash.exe",
            @"C:\Program Files (x86)\Git\bin\bash.exe",
        };

        /// Picks the shell for a command line. On Windows a `.sh` entry point is routed
        /// through Git for Windows' bash when available, so a repo shipping only run.sh
        /// still works there.
        public static (string File, string[] Args) Resolve(string command, OSKind os, Func<string, bool> fileExists)
        {
            if (os != OSKind.Windows) return ("/bin/sh", new[] { "-c", command });

            var first = command.TrimStart().Split(' ', 2)[0];
            if (first.EndsWith(".sh", StringComparison.OrdinalIgnoreCase))
            {
                var bash = GitBashCandidates.FirstOrDefault(fileExists);
                if (bash is not null) return (bash, new[] { "-c", command });
            }
            return ("cmd.exe", new[] { "/c", command });
        }

        public static (string File, string[] Args) Resolve(string command) =>
            Resolve(command, OSKinds.Current, File.Exists);
    }
}
```

Note: C# does not allow a file-scoped namespace followed by a block namespace. Split this into two files — `src/QuickRun.Core/OSKind.cs` for `OSKind`/`OSKinds` and `src/QuickRun.Core/Process/ShellCommand.cs` for the rest — and update the **Files** list accordingly.

- [ ] **Step 4: Run the shell tests and watch them pass**

Run: `dotnet test tests/QuickRun.Core.Tests --filter ShellCommandTests`
Expected: PASS, 4 tests.

- [ ] **Step 5: Write the failing runner test**

`tests/QuickRun.Core.Tests/CommandRunnerTests.cs`:

```csharp
using QuickRun.Core.Process;

namespace QuickRun.Core.Tests;

public class CommandRunnerTests
{
    [Fact]
    public void Capture_returns_stdout_and_exit_code()
    {
        var (file, args) = ShellCommand.Resolve("echo hello-quickrun");
        var result = CommandRunner.Capture(file, args);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello-quickrun", result.Output);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public void Capture_reports_a_nonzero_exit_code()
    {
        var (file, args) = ShellCommand.Resolve("exit 3");
        Assert.Equal(3, CommandRunner.Capture(file, args).ExitCode);
    }

    [Fact]
    public void Capture_reports_a_missing_executable_without_throwing()
    {
        var result = CommandRunner.Capture("definitely-not-a-real-binary-9876", Array.Empty<string>());
        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task StreamAsync_delivers_lines_as_they_arrive()
    {
        var lines = new List<string>();
        var code = await CommandRunner.StreamAsync(
            new ProcessSpec("echo one && echo two", null, null),
            (line, _) => { lock (lines) lines.Add(line); },
            CancellationToken.None);

        Assert.Equal(0, code);
        Assert.Contains("one", string.Join("\n", lines));
        Assert.Contains("two", string.Join("\n", lines));
    }

    [Fact]
    public async Task StreamAsync_passes_environment_variables_through()
    {
        var env = new Dictionary<string, string> { ["QUICKRUN_TEST_VALUE"] = "42" };
        var command = OSKinds.Current == OSKind.Windows ? "echo %QUICKRUN_TEST_VALUE%" : "echo $QUICKRUN_TEST_VALUE";
        var lines = new List<string>();
        await CommandRunner.StreamAsync(new ProcessSpec(command, null, env),
            (line, _) => { lock (lines) lines.Add(line); }, CancellationToken.None);
        Assert.Contains("42", string.Join("\n", lines));
    }
}
```

- [ ] **Step 6: Run it and watch it fail**

Run: `dotnet test tests/QuickRun.Core.Tests --filter CommandRunnerTests`
Expected: FAIL — `CommandRunner` does not exist.

- [ ] **Step 7: Implement `CommandRunner`**

`src/QuickRun.Core/Process/CommandRunner.cs`:

```csharp
using SysProcess = System.Diagnostics.Process;
using System.Diagnostics;

namespace QuickRun.Core.Process;

public sealed record CommandResult(int ExitCode, string Output, bool TimedOut);

public sealed record ProcessSpec(string Command, string? Cwd, IReadOnlyDictionary<string, string>? Env);

public static class CommandRunner
{
    public static CommandResult Capture(string file, IEnumerable<string> args, string? cwd = null,
        IReadOnlyDictionary<string, string>? env = null, int timeoutMs = 120_000)
    {
        var psi = Info(file, args, cwd, env);
        try
        {
            using var p = SysProcess.Start(psi);
            if (p is null) return new(-1, $"could not start {file}", false);
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs))
            {
                Kill(p);
                return new(-1, $"{file} timed out after {timeoutMs} ms", true);
            }
            return new(p.ExitCode, (stdout.Result + stderr.Result).Trim(), false);
        }
        catch (Exception e)
        {
            return new(-1, e.Message, false);
        }
    }

    public static async Task<int> StreamAsync(ProcessSpec spec, Action<string, bool> onLine, CancellationToken ct)
    {
        var (file, args) = ShellCommand.Resolve(spec.Command);
        var psi = Info(file, args, spec.Cwd, spec.Env);
        using var p = SysProcess.Start(psi);
        if (p is null) { onLine($"could not start: {spec.Command}", true); return -1; }

        p.OutputDataReceived += (_, e) => { if (e.Data is not null) onLine(e.Data, false); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) onLine(e.Data, true); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        using var reg = ct.Register(() => Kill(p));
        try { await p.WaitForExitAsync(CancellationToken.None); }
        finally { }
        return p.ExitCode;
    }

    private static ProcessStartInfo Info(string file, IEnumerable<string> args, string? cwd,
        IReadOnlyDictionary<string, string>? env)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = cwd ?? Environment.CurrentDirectory,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        if (env is not null)
            foreach (var kv in env) psi.Environment[kv.Key] = kv.Value;
        return psi;
    }

    private static void Kill(SysProcess p)
    {
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
    }
}
```

- [ ] **Step 8: Run all tests**

Run: `dotnet test tests/QuickRun.Core.Tests`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: command runner with shell resolution and process-tree kill"
```

---

### Task 3: Config model and shorthand-expanding parser

**Files:**
- Create: `src/QuickRun.Core/Config/ConfigModel.cs`
- Create: `src/QuickRun.Core/Config/ConfigParser.cs`
- Modify: `src/QuickRun.Core/QuickRun.Core.csproj` (add YamlDotNet)
- Test: `tests/QuickRun.Core.Tests/ConfigParserTests.cs`

**Interfaces:**
- Consumes: `OSKind` from Task 2.
- Produces:

```csharp
namespace QuickRun.Core.Config;

public enum InputType { Text, Password, Number, Bool, Select, Path, Dir, File }
public enum RestartPolicy { Never, OnFailure }

public sealed record ToolRequirement(string Tool, string? Version, string? Install, bool Optional);
public sealed record InputOption(string Value, string? Label);
public sealed record InputDef(string Id, string? Label, InputType Type, string? Description,
    string? Default, bool Required, string? Pattern, double? Min, double? Max,
    IReadOnlyList<InputOption> Options, string? Env, bool Persist);
public sealed record Step(string Run, string? Cwd, IReadOnlyList<string> When, bool ContinueOnError);
public sealed record ReadyWhen(int? Port, string? Http, string? Log, TimeSpan? Delay);
public sealed record TaskDef(string Name, string Run, string? Cwd,
    IReadOnlyDictionary<string, string> Env, IReadOnlyList<string> DependsOn,
    ReadyWhen? ReadyWhen, bool OpenReady, string? OpenUrl, RestartPolicy Restart);
public sealed record RunConfig(int Version, string? Name, string? Description, string? Icon, string? Docs,
    IReadOnlyList<ToolRequirement> Requires, IReadOnlyList<InputDef> Inputs,
    IReadOnlyDictionary<string, string> Env, IReadOnlyList<Step> Setup,
    IReadOnlyList<TaskDef> Tasks, IReadOnlyList<Step> Stop);

public sealed class ConfigException(string message) : Exception(message);

public static class ConfigParser
{
    public static readonly string[] FileNames = { "quickrun.yml", "quickrun.yaml" };
    public static RunConfig Parse(string yaml, OSKind os);
    public static string? FindConfigFile(string repoDir);
    public static string? FindRootScript(string repoDir, OSKind os);
}
```

- [ ] **Step 1: Add YamlDotNet**

```bash
dotnet add src/QuickRun.Core package YamlDotNet
```

Confirm the version landed in `Directory.Packages.props` and that the `PackageReference` in the csproj carries no `Version` attribute.

- [ ] **Step 2: Write the failing shorthand tests**

`tests/QuickRun.Core.Tests/ConfigParserTests.cs`:

```csharp
using QuickRun.Core;
using QuickRun.Core.Config;

namespace QuickRun.Core.Tests;

public class ConfigParserTests
{
    [Fact]
    public void Scalar_run_becomes_one_task_named_run()
    {
        var c = ConfigParser.Parse("run: ./run.sh", OSKind.Linux);
        var task = Assert.Single(c.Tasks);
        Assert.Equal("run", task.Name);
        Assert.Equal("./run.sh", task.Run);
        Assert.Equal(1, c.Version);
        Assert.Empty(c.Setup);
        Assert.Empty(c.Requires);
    }

    [Fact]
    public void Platform_map_picks_the_current_platform()
    {
        const string yaml = "run:\n  linux: ./run.sh\n  macos: ./run.sh\n  windows: ./run.ps1";
        Assert.Equal("./run.ps1", ConfigParser.Parse(yaml, OSKind.Windows).Tasks[0].Run);
        Assert.Equal("./run.sh", ConfigParser.Parse(yaml, OSKind.MacOs).Tasks[0].Run);
    }

    [Fact]
    public void Platform_map_without_an_entry_for_this_platform_throws()
    {
        var ex = Assert.Throws<ConfigException>(() => ConfigParser.Parse("run:\n  linux: ./run.sh", OSKind.Windows));
        Assert.Contains("windows", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_mapping_with_unknown_keys_is_not_a_platform_map()
    {
        Assert.Throws<ConfigException>(() => ConfigParser.Parse("run:\n  solaris: ./run.sh", OSKind.Linux));
    }

    [Fact]
    public void String_list_setup_becomes_sequential_steps()
    {
        var c = ConfigParser.Parse("setup: [npm ci, dotnet restore]\nrun: npm start", OSKind.Linux);
        Assert.Equal(new[] { "npm ci", "dotnet restore" }, c.Setup.Select(s => s.Run));
        Assert.All(c.Setup, s => Assert.False(s.ContinueOnError));
    }

    [Fact]
    public void String_list_tasks_get_generated_names()
    {
        var c = ConfigParser.Parse("tasks: [npm start, python api.py]", OSKind.Linux);
        Assert.Equal(new[] { "task-1", "task-2" }, c.Tasks.Select(t => t.Name));
    }

    [Fact]
    public void When_accepts_a_scalar_or_a_list()
    {
        var yaml = string.Join("\n",
            "setup:",
            "  - run: apt-get install -y libfoo",
            "    when: linux",
            "  - run: brew install foo",
            "    when: [macos]",
            "run: ./app");
        var c = ConfigParser.Parse(yaml, OSKind.Linux);
        Assert.Equal(new[] { "linux" }, c.Setup[0].When);
        Assert.Equal(new[] { "macos" }, c.Setup[1].When);
    }

    [Fact]
    public void Full_form_is_read_verbatim()
    {
        var yaml = string.Join("\n",
            "version: 1",
            "name: My App",
            "requires:",
            "  - tool: dotnet",
            "    version: \">=9.0\"",
            "    install: https://dot.net",
            "inputs:",
            "  - id: apiKey",
            "    type: password",
            "    required: true",
            "    env: OPENAI_API_KEY",
            "env:",
            "  ASPNETCORE_ENVIRONMENT: Development",
            "tasks:",
            "  - name: db",
            "    run: docker compose up -d db",
            "    readyWhen: {port: 5432}",
            "  - name: api",
            "    run: dotnet run",
            "    dependsOn: [db]",
            "    readyWhen: {http: \"http://localhost:5000\"}",
            "    open: true",
            "    restart: onFailure",
            "stop:",
            "  - docker compose down");
        var c = ConfigParser.Parse(yaml, OSKind.Linux);

        Assert.Equal("My App", c.Name);
        Assert.Equal(">=9.0", c.Requires[0].Version);
        Assert.False(c.Requires[0].Optional);
        Assert.Equal(InputType.Password, c.Inputs[0].Type);
        Assert.True(c.Inputs[0].Required);
        Assert.Equal("OPENAI_API_KEY", c.Inputs[0].Env);
        Assert.Equal("Development", c.Env["ASPNETCORE_ENVIRONMENT"]);
        Assert.Equal(5432, c.Tasks[0].ReadyWhen!.Port);
        Assert.Equal(new[] { "db" }, c.Tasks[1].DependsOn);
        Assert.True(c.Tasks[1].OpenReady);
        Assert.Null(c.Tasks[1].OpenUrl);
        Assert.Equal(RestartPolicy.OnFailure, c.Tasks[1].Restart);
        Assert.Equal("docker compose down", Assert.Single(c.Stop).Run);
    }

    [Fact]
    public void Open_with_a_url_sets_OpenUrl_and_not_OpenReady()
    {
        var c = ConfigParser.Parse("tasks:\n  - run: npm run dev\n    open: http://localhost:5173", OSKind.Linux);
        Assert.False(c.Tasks[0].OpenReady);
        Assert.Equal("http://localhost:5173", c.Tasks[0].OpenUrl);
    }

    [Fact]
    public void ReadyWhen_delay_parses_a_duration()
    {
        var c = ConfigParser.Parse("tasks:\n  - run: ./slow\n    readyWhen: {delay: 5s}", OSKind.Linux);
        Assert.Equal(TimeSpan.FromSeconds(5), c.Tasks[0].ReadyWhen!.Delay);
    }

    [Fact]
    public void Malformed_yaml_throws_ConfigException_not_a_yaml_exception()
    {
        Assert.Throws<ConfigException>(() => ConfigParser.Parse("run: [unclosed", OSKind.Linux));
    }

    [Fact]
    public void Unknown_top_level_keys_throw()
    {
        var ex = Assert.Throws<ConfigException>(() => ConfigParser.Parse("runn: ./run.sh", OSKind.Linux));
        Assert.Contains("runn", ex.Message);
    }

    [Fact]
    public void Both_run_and_tasks_is_rejected()
    {
        Assert.Throws<ConfigException>(() => ConfigParser.Parse("run: ./a\ntasks: [./b]", OSKind.Linux));
    }
}
```

- [ ] **Step 3: Run and watch them fail**

Run: `dotnet test tests/QuickRun.Core.Tests --filter ConfigParserTests`
Expected: FAIL — `ConfigParser` does not exist.

- [ ] **Step 4: Write `ConfigModel.cs`**

Copy the record and enum declarations from the **Interfaces** block above verbatim into `src/QuickRun.Core/Config/ConfigModel.cs`, plus this factory used by later tasks and tests:

```csharp
public static class RunConfigDefaults
{
    public static RunConfig Empty => new(1, null, null, null, null,
        Array.Empty<ToolRequirement>(), Array.Empty<InputDef>(),
        new Dictionary<string, string>(), Array.Empty<Step>(),
        Array.Empty<TaskDef>(), Array.Empty<Step>());
}
```

- [ ] **Step 5: Implement `ConfigParser`**

Deserialize to `object?` with YamlDotNet's untyped deserializer, then walk the tree by hand. Do not bind straight to the record types: shorthand means a node can be a string, a list, or a mapping, and hand-walking keeps that decision in one readable place.

```csharp
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace QuickRun.Core.Config;

public static class ConfigParser
{
    public static readonly string[] FileNames = { "quickrun.yml", "quickrun.yaml" };
    private static readonly string[] PlatformKeys = { "windows", "linux", "macos" };
    private static readonly string[] TopLevelKeys =
        { "version", "name", "description", "icon", "docs", "requires", "inputs", "env", "setup", "run", "tasks", "stop" };

    public static RunConfig Parse(string yaml, OSKind os)
    {
        Dictionary<string, object?> root;
        try
        {
            var raw = new DeserializerBuilder().Build().Deserialize<object?>(yaml);
            root = AsMap(raw) ?? throw new ConfigException("config must be a mapping of keys");
        }
        catch (YamlException e)
        {
            throw new ConfigException($"invalid YAML at line {e.Start.Line}: {e.Message}");
        }

        foreach (var key in root.Keys)
            if (!TopLevelKeys.Contains(key, StringComparer.Ordinal))
                throw new ConfigException($"unknown key '{key}' - expected one of {string.Join(", ", TopLevelKeys)}");

        return new RunConfig(
            Version: ParseInt(root.GetValueOrDefault("version")) ?? 1,
            Name: Str(root.GetValueOrDefault("name")),
            Description: Str(root.GetValueOrDefault("description")),
            Icon: Str(root.GetValueOrDefault("icon")),
            Docs: Str(root.GetValueOrDefault("docs")),
            Requires: ParseRequires(root.GetValueOrDefault("requires")),
            Inputs: ParseInputs(root.GetValueOrDefault("inputs")),
            Env: ParseStringMap(root.GetValueOrDefault("env")),
            Setup: ParseSteps(root.GetValueOrDefault("setup"), os, "setup"),
            Tasks: ParseTasks(root, os),
            Stop: ParseSteps(root.GetValueOrDefault("stop"), os, "stop"));
    }
}
```

Implement these private helpers in the same file. Each is a few lines; the shapes they must accept are exactly what the tests in Step 2 assert.

| Helper | Accepts | Behaviour |
|---|---|---|
| `AsMap(object?)` | YamlDotNet's `Dictionary<object,object?>` | returns `Dictionary<string,object?>` with ordinal comparison, or null when the node is not a mapping |
| `Str(object?)` | scalar | null stays null, everything else `ToString()` |
| `ParseInt` / `ParseBool` / `ParseDouble` | scalar | `ConfigException` on an unparseable non-null value |
| `StringList(object?)` | null, scalar, list | a scalar becomes a one-element list; null becomes empty |
| `ResolveCommand(node, os, path)` | string or platform map | mapping: every key must be in `PlatformKeys`, else `ConfigException($"{path}: unknown platform key '{k}'")`; missing entry for `os.Key()` throws `ConfigException($"{path}: no command for platform '{os.Key()}'")` |
| `ParseSteps(node, os, path)` | null, list of strings, list of mappings | mapping keys `run`, `cwd`, `when`, `continueOnError`; unknown keys throw; `when` through `StringList` |
| `ParseTasks(root, os)` | `run` or `tasks` | `run` present gives one `TaskDef` named `run`; `tasks` list names default to `task-{i+1}`; both present throws `ConfigException("use either 'run' or 'tasks', not both")`; neither present gives an empty list |
| `ParseReadyWhen(node, path)` | mapping with exactly one of `port`, `http`, `log`, `delay` | more than one throws; `delay` goes through `ParseDuration` |
| `ParseDuration(string)` | `5s`, `500ms`, `2m`, bare number means seconds | `ConfigException` otherwise |
| `ParseOpen(node)` | `true`, `false`, or a url string | returns `(bool OpenReady, string? OpenUrl)` |
| `ParseRestart(node)` | `never`, `onFailure`, absent | case-insensitive, absent means `Never`, anything else throws |
| `ParseRequires(node)` | list of mappings or strings | a bare string means `{tool: <string>}`; mapping keys `tool`, `version`, `install`, `optional` |
| `ParseInputs(node)` | list of mappings | `id` mandatory and matching `^[A-Za-z_][A-Za-z0-9_]*$`; `type` case-insensitive into `InputType`, default `Text`; `options` accepts scalars or `{value,label}` mappings |
| `ParseStringMap(node)` | mapping of scalars | values through `Str`, nulls become empty strings |

And the file-locating helpers:

```csharp
public static string? FindConfigFile(string repoDir) =>
    FileNames.Select(n => Path.Combine(repoDir, n)).FirstOrDefault(File.Exists);

public static string? FindRootScript(string repoDir, OSKind os)
{
    var order = os == OSKind.Windows
        ? new[] { "quickrun.ps1", "quickrun.sh", "run.ps1", "run.sh" }
        : new[] { "quickrun.sh", "run.sh" };
    return order.Select(n => Path.Combine(repoDir, n)).FirstOrDefault(File.Exists);
}
```

- [ ] **Step 6: Run the parser tests and watch them pass**

Run: `dotnet test tests/QuickRun.Core.Tests --filter ConfigParserTests`
Expected: PASS, 14 tests.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: quickrun.yml parser with shorthand expansion and platform maps"
```

---

### Task 4: Config validation

**Files:**
- Create: `src/QuickRun.Core/Config/ConfigValidator.cs`
- Test: `tests/QuickRun.Core.Tests/ConfigValidatorTests.cs`

**Interfaces:**
- Consumes: `RunConfig` and friends from Task 3.
- Produces:

```csharp
public sealed record ValidationIssue(string Path, string Message, bool IsError);

public static class ConfigValidator
{
    public static IReadOnlyList<ValidationIssue> Validate(RunConfig config);
}
```

The parser rejects malformed *syntax*; the validator rejects incoherent *content* — a `dependsOn` pointing at nothing, duplicate names, a `select` input with no options. Both are needed: the CLI's `validate` command and Phase 4's playground report the validator's issues, while the parser throws.

- [ ] **Step 1: Write the failing tests**

`tests/QuickRun.Core.Tests/ConfigValidatorTests.cs`:

```csharp
using QuickRun.Core;
using QuickRun.Core.Config;

namespace QuickRun.Core.Tests;

public class ConfigValidatorTests
{
    private static IReadOnlyList<ValidationIssue> Check(string yaml) =>
        ConfigValidator.Validate(ConfigParser.Parse(yaml, OSKind.Linux));

    private static void AssertError(IReadOnlyList<ValidationIssue> issues, string contains)
        => Assert.Contains(issues, i => i.IsError && i.Message.Contains(contains, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void A_minimal_config_is_valid()
        => Assert.Empty(Check("run: ./run.sh"));

    [Fact]
    public void A_config_with_nothing_to_execute_is_an_error()
        => AssertError(Check("name: Nothing"), "nothing to run");

    [Fact]
    public void Duplicate_task_names_are_an_error()
        => AssertError(Check("tasks:\n  - name: api\n    run: a\n  - name: api\n    run: b"), "duplicate");

    [Fact]
    public void Unknown_dependsOn_target_is_an_error()
        => AssertError(Check("tasks:\n  - name: api\n    run: a\n    dependsOn: [db]"), "dependsOn");

    [Fact]
    public void Dependency_cycles_are_an_error()
        => AssertError(Check("""
            tasks:
              - name: a
                run: x
                dependsOn: [b]
              - name: b
                run: y
                dependsOn: [a]
            """.ReplaceLineEndings("\n")), "cycle");

    [Fact]
    public void A_task_depending_on_itself_is_a_cycle()
        => AssertError(Check("tasks:\n  - name: a\n    run: x\n    dependsOn: [a]"), "cycle");

    [Fact]
    public void Duplicate_input_ids_are_an_error()
        => AssertError(Check("inputs:\n  - id: k\n  - id: k\nrun: a"), "duplicate");

    [Fact]
    public void A_select_input_without_options_is_an_error()
        => AssertError(Check("inputs:\n  - id: mode\n    type: select\nrun: a"), "options");

    [Fact]
    public void A_select_default_outside_its_options_is_an_error()
        => AssertError(Check("inputs:\n  - id: mode\n    type: select\n    options: [a, b]\n    default: c\nrun: x"), "default");

    [Fact]
    public void An_invalid_regex_pattern_is_an_error()
        => AssertError(Check("inputs:\n  - id: k\n    pattern: \"[unclosed\"\nrun: a"), "pattern");

    [Fact]
    public void Pattern_on_a_non_text_input_is_a_warning_not_an_error()
    {
        var issues = Check("inputs:\n  - id: k\n    type: bool\n    pattern: \"^x\"\nrun: a");
        Assert.Contains(issues, i => !i.IsError && i.Message.Contains("pattern", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(issues, i => i.IsError);
    }

    [Fact]
    public void An_unknown_interpolation_reference_is_an_error()
        => AssertError(Check("run: ./app --key ${inputs.missing}"), "missing");

    [Fact]
    public void A_known_interpolation_reference_is_accepted()
        => Assert.Empty(Check("inputs:\n  - id: apiKey\nrun: ./app --key ${inputs.apiKey}"));

    [Fact]
    public void An_unsupported_version_is_an_error()
        => AssertError(Check("version: 2\nrun: a"), "version");

    [Fact]
    public void An_absolute_cwd_is_an_error()
        => AssertError(Check("tasks:\n  - run: a\n    cwd: /etc"), "relative");

    [Fact]
    public void A_cwd_escaping_the_workspace_is_an_error()
        => AssertError(Check("tasks:\n  - run: a\n    cwd: ../../etc"), "outside");
}
```

- [ ] **Step 2: Run and watch them fail**

Run: `dotnet test tests/QuickRun.Core.Tests --filter ConfigValidatorTests`
Expected: FAIL — `ConfigValidator` does not exist.

- [ ] **Step 3: Implement `ConfigValidator`**

`src/QuickRun.Core/Config/ConfigValidator.cs`. Structure it as one method per rule group, each appending to a shared list:

```csharp
using System.Text.RegularExpressions;

namespace QuickRun.Core.Config;

public sealed record ValidationIssue(string Path, string Message, bool IsError);

public static class ConfigValidator
{
    private const int SupportedVersion = 1;

    public static IReadOnlyList<ValidationIssue> Validate(RunConfig c)
    {
        var issues = new List<ValidationIssue>();
        void Error(string path, string message) => issues.Add(new(path, message, true));
        void Warn(string path, string message) => issues.Add(new(path, message, false));

        if (c.Version != SupportedVersion)
            Error("version", $"unsupported version {c.Version}, this build understands version {SupportedVersion}");

        if (c.Tasks.Count == 0 && c.Setup.Count == 0)
            Error("", "nothing to run - add a 'run' command or a 'tasks' list");

        ValidateTasks(c, Error);
        ValidateInputs(c, Error, Warn);
        ValidateInterpolation(c, Error);
        ValidatePaths(c, Error);
        return issues;
    }
}
```

Rules to implement, one private method each:

**`ValidateTasks`**
- duplicate `Name` across tasks: `"duplicate task name '<name>'"`.
- each `DependsOn` entry must name an existing task: `"dependsOn references unknown task '<name>'"`.
- cycle detection by depth-first search with a visiting set; report `"dependency cycle: a -> b -> a"`. A self-reference is a cycle.
- a task whose `ReadyWhen` is null but which others `dependsOn`: warn `"'<name>' has no readyWhen, dependants start as soon as it launches"`.

**`ValidateInputs`**
- duplicate `Id`: `"duplicate input id '<id>'"`.
- `Type == Select` with empty `Options`: `"select input '<id>' needs options"`.
- `Default` not among `Options` for a select: `"default '<v>' is not one of the options"`.
- `Pattern` that fails `new Regex(pattern)`: `"invalid pattern: <regex error>"`.
- `Pattern` on a type other than `Text`/`Password`: warning `"pattern is ignored for type <type>"`.
- `Min`/`Max` on a non-`Number` type: warning. `Min > Max`: error.

**`ValidateInterpolation`**
- collect every interpolatable string: each task's `Run`, `Cwd`, env values, `OpenUrl`; each step's `Run`, `Cwd`; the top-level `Env` values.
- match `\$\{([a-zA-Z]+)\.([A-Za-z_][A-Za-z0-9_]*)\}` plus the bare forms `${workspace}`, `${repo.name}`, `${repo.ref}`.
- namespace `inputs` requires a matching `InputDef.Id`, else `"unknown input reference '<id>'"`. Namespace `env` is always accepted (resolved at run time). Any other namespace is an error.

**`ValidatePaths`**
- `Cwd` must be relative: `"cwd must be relative to the repository root"` for a rooted path.
- the normalised combination of a fake root with `Cwd` must stay under that root, else `"cwd points outside the repository"`. Use `Path.GetFullPath(Path.Combine("/w", cwd))` and check the prefix; do not touch the file system.

- [ ] **Step 4: Run and watch them pass**

Run: `dotnet test tests/QuickRun.Core.Tests --filter ConfigValidatorTests`
Expected: PASS, 17 tests.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: config validation with dependency cycle and interpolation checks"
```

---

### Task 5: Interpolation

**Files:**
- Create: `src/QuickRun.Core/Config/Interpolator.cs`
- Test: `tests/QuickRun.Core.Tests/InterpolatorTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:

```csharp
public sealed record InterpolationContext(
    IReadOnlyDictionary<string, string?> Inputs,
    string Workspace,
    string RepoName,
    string RepoRef,
    Func<string, string?>? EnvLookup = null);

public sealed class InterpolationException(string message) : Exception(message);

public static class Interpolator
{
    public static string Expand(string template, InterpolationContext ctx);
    public static IReadOnlyList<string> Secrets(IReadOnlyDictionary<string, string?> values, IEnumerable<string> secretIds);
    public static string Redact(string text, IReadOnlyList<string> secrets);
}
```

`EnvLookup` defaults to `Environment.GetEnvironmentVariable`. It is a parameter so tests never depend on the machine's environment.

- [ ] **Step 1: Write the failing tests**

`tests/QuickRun.Core.Tests/InterpolatorTests.cs`:

```csharp
using QuickRun.Core.Config;

namespace QuickRun.Core.Tests;

public class InterpolatorTests
{
    private static InterpolationContext Ctx(params (string Key, string? Value)[] inputs) =>
        new(inputs.ToDictionary(i => i.Key, i => i.Value),
            Workspace: "/w/acme__app__main",
            RepoName: "app",
            RepoRef: "main",
            EnvLookup: name => name == "HOME" ? "/home/tester" : null);

    [Fact]
    public void Expands_an_input_reference()
        => Assert.Equal("./app --key sk-1", Interpolator.Expand("./app --key ${inputs.apiKey}", Ctx(("apiKey", "sk-1"))));

    [Fact]
    public void Expands_workspace_repo_name_and_ref()
        => Assert.Equal("/w/acme__app__main app main",
            Interpolator.Expand("${workspace} ${repo.name} ${repo.ref}", Ctx()));

    [Fact]
    public void Expands_an_environment_reference()
        => Assert.Equal("/home/tester/x", Interpolator.Expand("${env.HOME}/x", Ctx()));

    [Fact]
    public void A_missing_environment_variable_expands_to_empty()
        => Assert.Equal("[]", Interpolator.Expand("[${env.NOT_SET_ANYWHERE}]", Ctx()));

    [Fact]
    public void A_null_input_expands_to_empty()
        => Assert.Equal("[]", Interpolator.Expand("[${inputs.optional}]", Ctx(("optional", null))));

    [Fact]
    public void An_unknown_input_throws_and_names_the_key()
    {
        var ex = Assert.Throws<InterpolationException>(() => Interpolator.Expand("${inputs.nope}", Ctx()));
        Assert.Contains("nope", ex.Message);
    }

    [Fact]
    public void An_unknown_namespace_throws()
        => Assert.Throws<InterpolationException>(() => Interpolator.Expand("${secrets.k}", Ctx()));

    [Fact]
    public void Text_without_placeholders_is_returned_unchanged()
        => Assert.Equal("npm run dev", Interpolator.Expand("npm run dev", Ctx()));

    [Fact]
    public void Redact_replaces_every_secret_occurrence()
    {
        var secrets = Interpolator.Secrets(
            new Dictionary<string, string?> { ["apiKey"] = "sk-abc", ["mode"] = "dev" },
            new[] { "apiKey" });
        Assert.Equal("using *** twice: ***", Interpolator.Redact("using sk-abc twice: sk-abc", secrets));
    }

    [Fact]
    public void Redact_ignores_empty_and_very_short_secrets()
    {
        var secrets = Interpolator.Secrets(
            new Dictionary<string, string?> { ["a"] = "", ["b"] = "x" }, new[] { "a", "b" });
        Assert.Equal("keeps x intact", Interpolator.Redact("keeps x intact", secrets));
    }
}
```

The last test locks in a deliberate rule: secrets shorter than four characters are not redacted, because blanket-replacing a one-character value mangles every log line it happens to appear in.

- [ ] **Step 2: Run and watch them fail**

Run: `dotnet test tests/QuickRun.Core.Tests --filter InterpolatorTests`
Expected: FAIL — `Interpolator` does not exist.

- [ ] **Step 3: Implement `Interpolator`**

```csharp
using System.Text.RegularExpressions;

namespace QuickRun.Core.Config;

public sealed record InterpolationContext(
    IReadOnlyDictionary<string, string?> Inputs, string Workspace, string RepoName, string RepoRef,
    Func<string, string?>? EnvLookup = null);

public sealed class InterpolationException(string message) : Exception(message);

public static partial class Interpolator
{
    private const int MinRedactableLength = 4;

    public static string Expand(string template, InterpolationContext ctx) =>
        Placeholder().Replace(template, m => Resolve(m.Groups[1].Value, ctx));

    public static IReadOnlyList<string> Secrets(IReadOnlyDictionary<string, string?> values, IEnumerable<string> secretIds) =>
        secretIds.Select(id => values.GetValueOrDefault(id))
                 .Where(v => !string.IsNullOrEmpty(v) && v!.Length >= MinRedactableLength)
                 .Select(v => v!)
                 .Distinct(StringComparer.Ordinal)
                 .ToList();

    public static string Redact(string text, IReadOnlyList<string> secrets)
    {
        foreach (var s in secrets) text = text.Replace(s, "***", StringComparison.Ordinal);
        return text;
    }

    private static string Resolve(string expression, InterpolationContext ctx)
    {
        switch (expression)
        {
            case "workspace": return ctx.Workspace;
            case "repo.name": return ctx.RepoName;
            case "repo.ref": return ctx.RepoRef;
        }

        var parts = expression.Split('.', 2);
        if (parts.Length != 2) throw new InterpolationException($"unknown placeholder '${{{expression}}}'");

        return parts[0] switch
        {
            "inputs" => ctx.Inputs.TryGetValue(parts[1], out var v)
                ? v ?? ""
                : throw new InterpolationException($"unknown input reference '{parts[1]}'"),
            "env" => (ctx.EnvLookup ?? Environment.GetEnvironmentVariable)(parts[1]) ?? "",
            _ => throw new InterpolationException($"unknown placeholder namespace '{parts[0]}'"),
        };
    }

    [GeneratedRegex(@"\$\{([A-Za-z_][A-Za-z0-9_.]*)\}")]
    private static partial Regex Placeholder();
}
```

- [ ] **Step 4: Run and watch them pass**

Run: `dotnet test tests/QuickRun.Core.Tests --filter InterpolatorTests`
Expected: PASS, 10 tests.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: placeholder interpolation and secret redaction"
```

---

### Task 6: Prerequisite checks

**Files:**
- Create: `src/QuickRun.Core/Requires/ToolChecker.cs`
- Test: `tests/QuickRun.Core.Tests/ToolCheckerTests.cs`

**Interfaces:**
- Consumes: `VersionCheck` (Task 1), `CommandRunner` (Task 2), `ToolRequirement` (Task 3).
- Produces:

```csharp
public sealed record ToolCheckResult(ToolRequirement Requirement, bool Found, string? FoundVersion, bool Satisfied)
{
    public bool Blocks => !Satisfied && !Requirement.Optional;
    public string Describe();   // "dotnet >=9.0 - found 10.0.300" / "node >=20 - not installed"
}

public static class ToolChecker
{
    public static string[] ProbeArgs(string tool);
    public static ToolCheckResult Check(ToolRequirement requirement, Func<string, string[], CommandResult>? runner = null);
    public static IReadOnlyList<ToolCheckResult> CheckAll(IEnumerable<ToolRequirement> requirements);
}
```

The `runner` parameter exists so tests never depend on which tools the machine happens to have. It defaults to `(file, args) => CommandRunner.Capture(file, args, timeoutMs: 15_000)`.

- [ ] **Step 1: Write the failing tests**

`tests/QuickRun.Core.Tests/ToolCheckerTests.cs`:

```csharp
using QuickRun.Core.Config;
using QuickRun.Core.Process;
using QuickRun.Core.Requires;

namespace QuickRun.Core.Tests;

public class ToolCheckerTests
{
    private static ToolRequirement Req(string tool, string? version = null, bool optional = false)
        => new(tool, version, null, optional);

    private static Func<string, string[], CommandResult> Fake(string output, int exit = 0)
        => (_, _) => new CommandResult(exit, output, false);

    [Theory]
    [InlineData("node", "-v")]
    [InlineData("npm", "-v")]
    [InlineData("dotnet", "--version")]
    [InlineData("java", "-version")]
    [InlineData("go", "version")]
    [InlineData("some-random-tool", "--version")]
    public void ProbeArgs_knows_the_common_tools(string tool, string expected)
        => Assert.Equal(expected, ToolChecker.ProbeArgs(tool).Single());

    [Fact]
    public void A_satisfied_requirement_reports_the_found_version()
    {
        var r = ToolChecker.Check(Req("dotnet", ">=9.0"), Fake("10.0.300"));
        Assert.True(r.Found);
        Assert.Equal("10.0.300", r.FoundVersion);
        Assert.True(r.Satisfied);
        Assert.False(r.Blocks);
    }

    [Fact]
    public void A_version_below_the_range_is_not_satisfied()
    {
        var r = ToolChecker.Check(Req("dotnet", ">=9.0"), Fake("8.0.404"));
        Assert.True(r.Found);
        Assert.False(r.Satisfied);
        Assert.True(r.Blocks);
    }

    [Fact]
    public void A_missing_tool_is_not_found()
    {
        var r = ToolChecker.Check(Req("nope"), Fake("command not found", exit: 127));
        Assert.False(r.Found);
        Assert.Null(r.FoundVersion);
        Assert.True(r.Blocks);
    }

    [Fact]
    public void A_missing_optional_tool_does_not_block()
        => Assert.False(ToolChecker.Check(Req("nope", optional: true), Fake("", exit: 127)).Blocks);

    [Fact]
    public void A_tool_present_without_a_version_requirement_is_satisfied()
    {
        var r = ToolChecker.Check(Req("docker"), Fake("Docker version 27.3.1, build ce12230"));
        Assert.True(r.Satisfied);
        Assert.Equal("27.3.1", r.FoundVersion);
    }

    [Fact]
    public void A_tool_that_exits_zero_but_prints_no_version_is_still_found()
    {
        var r = ToolChecker.Check(Req("weird"), Fake("hello"));
        Assert.True(r.Found);
        Assert.Null(r.FoundVersion);
        Assert.True(r.Satisfied);
    }

    [Fact]
    public void A_tool_that_exits_zero_without_a_version_fails_a_version_requirement()
        => Assert.False(ToolChecker.Check(Req("weird", ">=1.0"), Fake("hello")).Satisfied);

    [Fact]
    public void Describe_mentions_the_tool_and_the_outcome()
    {
        Assert.Contains("not installed", ToolChecker.Check(Req("nope"), Fake("", exit: 127)).Describe());
        Assert.Contains("10.0.300", ToolChecker.Check(Req("dotnet", ">=9.0"), Fake("10.0.300")).Describe());
    }
}
```

- [ ] **Step 2: Run and watch them fail**

Run: `dotnet test tests/QuickRun.Core.Tests --filter ToolCheckerTests`
Expected: FAIL — `ToolChecker` does not exist.

- [ ] **Step 3: Implement `ToolChecker`**

```csharp
using QuickRun.Core.Config;
using QuickRun.Core.Process;

namespace QuickRun.Core.Requires;

public sealed record ToolCheckResult(ToolRequirement Requirement, bool Found, string? FoundVersion, bool Satisfied)
{
    public bool Blocks => !Satisfied && !Requirement.Optional;

    public string Describe()
    {
        var want = string.IsNullOrWhiteSpace(Requirement.Version) ? "" : " " + Requirement.Version;
        if (!Found) return $"{Requirement.Tool}{want} - not installed";
        var found = FoundVersion is null ? "present" : FoundVersion;
        return $"{Requirement.Tool}{want} - found {found}";
    }
}

public static class ToolChecker
{
    private static readonly Dictionary<string, string> KnownProbes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["node"] = "-v", ["npm"] = "-v", ["pnpm"] = "-v", ["yarn"] = "-v",
        ["java"] = "-version", ["go"] = "version", ["mvn"] = "-v",
    };

    public static string[] ProbeArgs(string tool) =>
        new[] { KnownProbes.GetValueOrDefault(tool, "--version") };

    public static ToolCheckResult Check(ToolRequirement requirement, Func<string, string[], CommandResult>? runner = null)
    {
        runner ??= (file, args) => CommandRunner.Capture(file, args, timeoutMs: 15_000);
        var result = runner(requirement.Tool, ProbeArgs(requirement.Tool));

        if (result.ExitCode != 0)
            return new(requirement, false, null, false);

        var version = VersionCheck.Extract(result.Output);
        var satisfied = string.IsNullOrWhiteSpace(requirement.Version)
            ? true
            : VersionCheck.Satisfies(version, requirement.Version);
        return new(requirement, true, version, satisfied);
    }

    public static IReadOnlyList<ToolCheckResult> CheckAll(IEnumerable<ToolRequirement> requirements) =>
        requirements.Select(r => Check(r)).ToList();
}
```

Note: probing goes through the shell rather than launching the tool directly, so that shell builtins and `.cmd` shims on Windows (`npm.cmd`) resolve. Wrap the default runner accordingly:

```csharp
runner ??= (file, args) =>
{
    var (shell, shellArgs) = ShellCommand.Resolve($"{file} {string.Join(' ', args)}");
    return CommandRunner.Capture(shell, shellArgs, timeoutMs: 15_000);
};
```

- [ ] **Step 4: Run and watch them pass**

Run: `dotnet test tests/QuickRun.Core.Tests --filter ToolCheckerTests`
Expected: PASS, 14 tests.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: prerequisite tool checks with version probing"
```

---

### Task 7: Input resolution

**Files:**
- Create: `src/QuickRun.Core/Inputs/InputResolver.cs`
- Test: `tests/QuickRun.Core.Tests/InputResolverTests.cs`

**Interfaces:**
- Consumes: `InputDef`, `InputType` (Task 3).
- Produces:

```csharp
public sealed record InputError(string Id, string Message);

public static class InputResolver
{
    public static IReadOnlyDictionary<string, string?> ApplyDefaults(
        IReadOnlyList<InputDef> defs, IReadOnlyDictionary<string, string?> provided);

    public static IReadOnlyList<InputError> Validate(
        IReadOnlyList<InputDef> defs, IReadOnlyDictionary<string, string?> values);

    public static IReadOnlyDictionary<string, string> ToEnv(
        IReadOnlyList<InputDef> defs, IReadOnlyDictionary<string, string?> values);

    public static IReadOnlyList<string> SecretIds(IReadOnlyList<InputDef> defs);

    public static IReadOnlyDictionary<string, string?> ParseAssignments(IEnumerable<string> assignments);
}
```

`ParseAssignments` turns the CLI's `--input key=value` occurrences into a dictionary; a value containing `=` keeps everything after the first one.

- [ ] **Step 1: Write the failing tests**

`tests/QuickRun.Core.Tests/InputResolverTests.cs`:

```csharp
using QuickRun.Core.Config;
using QuickRun.Core.Inputs;

namespace QuickRun.Core.Tests;

public class InputResolverTests
{
    private static InputDef Def(string id, InputType type = InputType.Text, bool required = false,
        string? def = null, string? pattern = null, double? min = null, double? max = null,
        string[]? options = null, string? env = null) =>
        new(id, null, type, null, def, required, pattern, min, max,
            (options ?? Array.Empty<string>()).Select(o => new InputOption(o, null)).ToList(), env, false);

    private static Dictionary<string, string?> Values(params (string, string?)[] v) =>
        v.ToDictionary(x => x.Item1, x => x.Item2);

    [Fact]
    public void ApplyDefaults_fills_missing_values()
    {
        var result = InputResolver.ApplyDefaults(new[] { Def("mode", def: "dev") }, Values());
        Assert.Equal("dev", result["mode"]);
    }

    [Fact]
    public void ApplyDefaults_does_not_overwrite_a_provided_value()
    {
        var result = InputResolver.ApplyDefaults(new[] { Def("mode", def: "dev") }, Values(("mode", "prod")));
        Assert.Equal("prod", result["mode"]);
    }

    [Fact]
    public void A_missing_required_value_is_an_error()
    {
        var errors = InputResolver.Validate(new[] { Def("apiKey", required: true) }, Values());
        Assert.Equal("apiKey", Assert.Single(errors).Id);
    }

    [Fact]
    public void An_empty_string_does_not_satisfy_required()
        => Assert.Single(InputResolver.Validate(new[] { Def("apiKey", required: true) }, Values(("apiKey", "  "))));

    [Fact]
    public void A_missing_optional_value_is_fine()
        => Assert.Empty(InputResolver.Validate(new[] { Def("note") }, Values()));

    [Fact]
    public void A_value_failing_the_pattern_is_an_error()
        => Assert.Single(InputResolver.Validate(new[] { Def("k", pattern: "^sk-") }, Values(("k", "nope"))));

    [Fact]
    public void A_value_matching_the_pattern_is_accepted()
        => Assert.Empty(InputResolver.Validate(new[] { Def("k", pattern: "^sk-") }, Values(("k", "sk-1"))));

    [Fact]
    public void A_non_numeric_value_for_a_number_input_is_an_error()
        => Assert.Single(InputResolver.Validate(new[] { Def("port", InputType.Number) }, Values(("port", "abc"))));

    [Fact]
    public void A_number_outside_min_max_is_an_error()
        => Assert.Single(InputResolver.Validate(
            new[] { Def("port", InputType.Number, min: 1, max: 65535) }, Values(("port", "70000"))));

    [Fact]
    public void A_non_boolean_value_for_a_bool_input_is_an_error()
        => Assert.Single(InputResolver.Validate(new[] { Def("flag", InputType.Bool) }, Values(("flag", "maybe"))));

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("True")]
    public void Boolean_values_are_accepted_case_insensitively(string value)
        => Assert.Empty(InputResolver.Validate(new[] { Def("flag", InputType.Bool) }, Values(("flag", value))));

    [Fact]
    public void A_select_value_outside_its_options_is_an_error()
        => Assert.Single(InputResolver.Validate(
            new[] { Def("mode", InputType.Select, options: new[] { "dev", "prod" }) }, Values(("mode", "staging"))));

    [Fact]
    public void ToEnv_maps_only_inputs_that_declare_env()
    {
        var env = InputResolver.ToEnv(
            new[] { Def("apiKey", env: "OPENAI_API_KEY"), Def("note") },
            Values(("apiKey", "sk-1"), ("note", "hello")));
        Assert.Equal("sk-1", env["OPENAI_API_KEY"]);
        Assert.Single(env);
    }

    [Fact]
    public void ToEnv_skips_null_values()
        => Assert.Empty(InputResolver.ToEnv(new[] { Def("k", env: "K") }, Values(("k", null))));

    [Fact]
    public void SecretIds_lists_password_inputs()
        => Assert.Equal(new[] { "pw" },
            InputResolver.SecretIds(new[] { Def("pw", InputType.Password), Def("plain") }));

    [Fact]
    public void ParseAssignments_splits_on_the_first_equals_sign()
    {
        var parsed = InputResolver.ParseAssignments(new[] { "apiKey=sk-a=b", "mode=dev" });
        Assert.Equal("sk-a=b", parsed["apiKey"]);
        Assert.Equal("dev", parsed["mode"]);
    }

    [Fact]
    public void ParseAssignments_rejects_a_value_without_an_equals_sign()
        => Assert.Throws<ArgumentException>(() => InputResolver.ParseAssignments(new[] { "apiKey" }));
}
```

- [ ] **Step 2: Run and watch them fail**

Run: `dotnet test tests/QuickRun.Core.Tests --filter InputResolverTests`
Expected: FAIL — `InputResolver` does not exist.

- [ ] **Step 3: Implement `InputResolver`**

One method per member of the interface block. Validation collects rather than throws, so a form can show every problem at once:

```csharp
using System.Globalization;
using System.Text.RegularExpressions;
using QuickRun.Core.Config;

namespace QuickRun.Core.Inputs;

public sealed record InputError(string Id, string Message);

public static class InputResolver
{
    public static IReadOnlyDictionary<string, string?> ApplyDefaults(
        IReadOnlyList<InputDef> defs, IReadOnlyDictionary<string, string?> provided)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var d in defs)
            result[d.Id] = provided.TryGetValue(d.Id, out var v) && v is not null ? v : d.Default;
        return result;
    }

    public static IReadOnlyList<InputError> Validate(
        IReadOnlyList<InputDef> defs, IReadOnlyDictionary<string, string?> values)
    {
        var errors = new List<InputError>();
        foreach (var d in defs)
        {
            var raw = values.GetValueOrDefault(d.Id);
            var empty = string.IsNullOrWhiteSpace(raw);

            if (d.Required && empty) { errors.Add(new(d.Id, $"'{d.Id}' is required")); continue; }
            if (empty) continue;

            switch (d.Type)
            {
                case InputType.Number:
                    if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
                        errors.Add(new(d.Id, $"'{d.Id}' must be a number"));
                    else if (d.Min is { } min && n < min) errors.Add(new(d.Id, $"'{d.Id}' must be at least {min}"));
                    else if (d.Max is { } max && n > max) errors.Add(new(d.Id, $"'{d.Id}' must be at most {max}"));
                    break;
                case InputType.Bool:
                    if (!bool.TryParse(raw, out _)) errors.Add(new(d.Id, $"'{d.Id}' must be true or false"));
                    break;
                case InputType.Select:
                    if (!d.Options.Any(o => string.Equals(o.Value, raw, StringComparison.Ordinal)))
                        errors.Add(new(d.Id, $"'{d.Id}' must be one of {string.Join(", ", d.Options.Select(o => o.Value))}"));
                    break;
                default:
                    if (!string.IsNullOrWhiteSpace(d.Pattern) && !Regex.IsMatch(raw!, d.Pattern))
                        errors.Add(new(d.Id, $"'{d.Id}' does not match {d.Pattern}"));
                    break;
            }
        }
        return errors;
    }

    public static IReadOnlyDictionary<string, string> ToEnv(
        IReadOnlyList<InputDef> defs, IReadOnlyDictionary<string, string?> values) =>
        defs.Where(d => !string.IsNullOrWhiteSpace(d.Env))
            .Select(d => (Name: d.Env!, Value: values.GetValueOrDefault(d.Id)))
            .Where(x => x.Value is not null)
            .ToDictionary(x => x.Name, x => x.Value!, StringComparer.Ordinal);

    public static IReadOnlyList<string> SecretIds(IReadOnlyList<InputDef> defs) =>
        defs.Where(d => d.Type == InputType.Password).Select(d => d.Id).ToList();

    public static IReadOnlyDictionary<string, string?> ParseAssignments(IEnumerable<string> assignments)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var a in assignments)
        {
            var i = a.IndexOf('=');
            if (i <= 0) throw new ArgumentException($"expected key=value, got '{a}'");
            result[a[..i]] = a[(i + 1)..];
        }
        return result;
    }
}
```

Note: the pattern check runs for `Text`, `Password`, `Path`, `Dir` and `File` because they all fall into `default`. Existence checks for `Dir` and `File` are deliberately left out of Core — the value is relative to a workspace that does not exist yet at form-fill time.

- [ ] **Step 4: Run and watch them pass**

Run: `dotnet test tests/QuickRun.Core.Tests --filter InputResolverTests`
Expected: PASS, 19 tests.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: input defaulting, validation and env mapping"
```

---

### Task 8: Workspace store

**Files:**
- Create: `src/QuickRun.Core/Workspace/WorkspaceStore.cs`
- Test: `tests/QuickRun.Core.Tests/WorkspaceStoreTests.cs`, `tests/QuickRun.Core.Tests/TempHome.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:

```csharp
public sealed record WorkspaceInfo(string Id, string Path, string Repo, string Ref,
    long Bytes, DateTimeOffset LastUsed, string? LastCommit, bool? LastOk);

public sealed class WorkspaceStore
{
    public WorkspaceStore(string? rootOverride = null);
    public string Root { get; }
    public static string IdFor(string repoUrl, string @ref);
    public string PathFor(string repoUrl, string @ref);
    public IReadOnlyList<WorkspaceInfo> List();
    public WorkspaceInfo? Get(string id);
    public void Touch(string id, string repoUrl, string @ref, string? commit, bool? ok);
    public bool Remove(string id);
    public int Clean(TimeSpan olderThan);
    public int RemoveAll();
}
```

Root resolution order: `rootOverride`, then `QUICKRUN_HOME` environment variable, then
`Environment.GetFolderPath(SpecialFolder.LocalApplicationData)` joined with `QuickRun`. Runs live in
`<root>/runs/<id>`; metadata for a workspace lives in `<root>/runs/<id>/.quickrun-meta.json`, which is
inside the workspace so that deleting the directory deletes its metadata.

`GetFolderPath(LocalApplicationData)` already maps to `%LOCALAPPDATA%` on Windows,
`~/.local/share` on Linux and `~/Library/Application Support` on macOS, which is exactly the spec's
table — no per-platform branching needed.

- [ ] **Step 1: Write the shared temp-home helper**

`tests/QuickRun.Core.Tests/TempHome.cs`:

```csharp
namespace QuickRun.Core.Tests;

/// A disposable directory used as QUICKRUN_HOME so workspace tests never touch the real one.
public sealed class TempHome : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "quickrun-tests-" + Guid.NewGuid().ToString("n")[..8]);

    public TempHome() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { }
    }
}
```

- [ ] **Step 2: Write the failing tests**

`tests/QuickRun.Core.Tests/WorkspaceStoreTests.cs`:

```csharp
using QuickRun.Core.Workspace;

namespace QuickRun.Core.Tests;

public class WorkspaceStoreTests
{
    [Theory]
    [InlineData("https://github.com/acme/app", "main", "acme__app__main")]
    [InlineData("https://github.com/acme/app.git", "main", "acme__app__main")]
    [InlineData("git@github.com:acme/app.git", "main", "acme__app__main")]
    [InlineData("https://github.com/acme/app", "feature/login", "acme__app__feature__login")]
    public void IdFor_produces_a_readable_filesystem_safe_id(string repo, string @ref, string expected)
        => Assert.StartsWith(expected, WorkspaceStore.IdFor(repo, @ref));

    [Fact]
    public void IdFor_disambiguates_refs_that_sanitise_to_the_same_name()
    {
        var a = WorkspaceStore.IdFor("https://github.com/acme/app", "feature/login");
        var b = WorkspaceStore.IdFor("https://github.com/acme/app", "feature__login");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void IdFor_is_stable_across_calls()
        => Assert.Equal(
            WorkspaceStore.IdFor("https://github.com/acme/app", "main"),
            WorkspaceStore.IdFor("https://github.com/acme/app", "main"));

    [Fact]
    public void IdFor_strips_characters_that_are_illegal_on_windows()
    {
        var id = WorkspaceStore.IdFor("https://github.com/acme/app", "fix:colon?star*");
        Assert.DoesNotContain(':', id);
        Assert.DoesNotContain('?', id);
        Assert.DoesNotContain('*', id);
    }

    [Fact]
    public void An_empty_store_lists_nothing()
    {
        using var home = new TempHome();
        Assert.Empty(new WorkspaceStore(home.Path).List());
    }

    [Fact]
    public void Touch_registers_a_workspace_that_List_then_returns()
    {
        using var home = new TempHome();
        var store = new WorkspaceStore(home.Path);
        var id = WorkspaceStore.IdFor("https://github.com/acme/app", "main");
        Directory.CreateDirectory(store.PathFor("https://github.com/acme/app", "main"));

        store.Touch(id, "https://github.com/acme/app", "main", "abc1234", true);

        var info = Assert.Single(store.List());
        Assert.Equal(id, info.Id);
        Assert.Equal("main", info.Ref);
        Assert.Equal("abc1234", info.LastCommit);
        Assert.True(info.LastOk);
    }

    [Fact]
    public void List_reports_the_size_on_disk()
    {
        using var home = new TempHome();
        var store = new WorkspaceStore(home.Path);
        var path = store.PathFor("https://github.com/acme/app", "main");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "big.txt"), new string('x', 5000));
        store.Touch(WorkspaceStore.IdFor("https://github.com/acme/app", "main"),
            "https://github.com/acme/app", "main", null, null);

        Assert.True(Assert.Single(store.List()).Bytes >= 5000);
    }

    [Fact]
    public void Remove_deletes_the_directory_and_returns_true()
    {
        using var home = new TempHome();
        var store = new WorkspaceStore(home.Path);
        var path = store.PathFor("https://github.com/acme/app", "main");
        Directory.CreateDirectory(path);
        var id = WorkspaceStore.IdFor("https://github.com/acme/app", "main");
        store.Touch(id, "https://github.com/acme/app", "main", null, null);

        Assert.True(store.Remove(id));
        Assert.False(Directory.Exists(path));
        Assert.Empty(store.List());
    }

    [Fact]
    public void Remove_returns_false_for_an_unknown_id()
        => Assert.False(new WorkspaceStore(new TempHome().Path).Remove("nope"));

    [Fact]
    public void Remove_refuses_an_id_that_tries_to_escape_the_root()
    {
        using var home = new TempHome();
        Assert.Throws<ArgumentException>(() => new WorkspaceStore(home.Path).Remove("../../windows"));
    }

    [Fact]
    public void Clean_removes_only_workspaces_older_than_the_cutoff()
    {
        using var home = new TempHome();
        var store = new WorkspaceStore(home.Path);

        var oldPath = store.PathFor("https://github.com/acme/old", "main");
        var newPath = store.PathFor("https://github.com/acme/new", "main");
        Directory.CreateDirectory(oldPath);
        Directory.CreateDirectory(newPath);
        store.Touch(WorkspaceStore.IdFor("https://github.com/acme/old", "main"),
            "https://github.com/acme/old", "main", null, null);
        store.Touch(WorkspaceStore.IdFor("https://github.com/acme/new", "main"),
            "https://github.com/acme/new", "main", null, null);

        // age the old one by rewriting its metadata timestamp
        var meta = Path.Combine(oldPath, ".quickrun-meta.json");
        File.WriteAllText(meta, File.ReadAllText(meta).Replace(
            DateTimeOffset.UtcNow.Year.ToString(), (DateTimeOffset.UtcNow.Year - 2).ToString()));

        Assert.Equal(1, store.Clean(TimeSpan.FromDays(30)));
        Assert.False(Directory.Exists(oldPath));
        Assert.True(Directory.Exists(newPath));
    }

    [Fact]
    public void RemoveAll_empties_the_store()
    {
        using var home = new TempHome();
        var store = new WorkspaceStore(home.Path);
        foreach (var name in new[] { "a", "b" })
        {
            Directory.CreateDirectory(store.PathFor($"https://github.com/acme/{name}", "main"));
            store.Touch(WorkspaceStore.IdFor($"https://github.com/acme/{name}", "main"),
                $"https://github.com/acme/{name}", "main", null, null);
        }
        Assert.Equal(2, store.RemoveAll());
        Assert.Empty(store.List());
    }
}
```

The `Clean` test's timestamp rewrite is crude on purpose — it avoids introducing a clock abstraction for a single test. If it proves brittle, add an internal `Func<DateTimeOffset> now` constructor parameter and set it from the test instead.

- [ ] **Step 3: Run and watch them fail**

Run: `dotnet test tests/QuickRun.Core.Tests --filter WorkspaceStoreTests`
Expected: FAIL — `WorkspaceStore` does not exist.

- [ ] **Step 4: Implement `WorkspaceStore`**

Key points for the implementer:

- `IdFor` builds `<owner>__<repo>__<sanitised-ref>` then appends `-` plus the first 6 hex characters of a SHA-256 over the *unsanitised* `repoUrl + "\n" + ref`. That is what makes the disambiguation test pass while keeping the name readable. Owner and repo come from splitting the URL on `/` and `:` and dropping a trailing `.git`.
- Sanitisation replaces every character in `Path.GetInvalidFileNameChars()` plus `/` and `\` with `__`, collapses runs of `_` longer than two, and trims to 80 characters.
- `Remove` and `Get` must reject an id containing a path separator or `..` with `ArgumentException` before touching the file system.
- Metadata is `System.Text.Json` over a private record `Meta(string Repo, string Ref, DateTimeOffset LastUsed, string? LastCommit, bool? LastOk)`, serialised with `JsonSerializerDefaults.Web`.
- `List` enumerates `<root>/runs/*` directories, reads each `.quickrun-meta.json` (skipping directories without one), computes size with `Directory.EnumerateFiles(..., SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length)` inside a try/catch, and orders by `LastUsed` descending.
- `Clean(olderThan)` removes every workspace whose `LastUsed` is older than `DateTimeOffset.UtcNow - olderThan` and returns the count.

Size computation walks the whole tree, which is slow for a workspace holding `node_modules`.
`// ponytail: full tree walk per List() call, cache in metadata if the workspace list feels slow`

- [ ] **Step 5: Run and watch them pass**

Run: `dotnet test tests/QuickRun.Core.Tests --filter WorkspaceStoreTests`
Expected: PASS, 15 tests.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: managed workspace store with listing, sizing and cleanup"
```

---

### Task 9: Git checkout and credential resolution

**Files:**
- Create: `src/QuickRun.Core/Git/CredentialResolver.cs`
- Create: `src/QuickRun.Core/Git/GitClient.cs`
- Test: `tests/QuickRun.Core.Tests/GitClientTests.cs`, `tests/QuickRun.Core.Tests/LocalRepo.cs`

**Interfaces:**
- Consumes: `CommandRunner` (Task 2).
- Produces:

```csharp
public sealed record GitOutcome(bool Ok, string? Error, string? Commit);

public sealed class CredentialResolver
{
    public CredentialResolver(string? explicitToken = null, Func<string, string[], CommandResult>? runner = null);
    public string? Resolve(string host);
}

public sealed class GitClient
{
    public GitClient(CredentialResolver credentials, Func<string, string[], string?, CommandResult>? runner = null);
    public static string NormalizeRepoUrl(string input);
    public static string HostOf(string repoUrl);
    internal static string AuthUrl(string url, string? token);
    internal static string Scrub(string text, string? token);
    public GitOutcome CheckoutOrUpdate(string repoUrl, string @ref, int? pullRequest, string targetDir, bool fresh);
    public (IReadOnlyList<string>? Branches, string? Error) ListBranches(string repoUrl);
    public string? HeadCommit(string dir);
}
```

`NormalizeRepoUrl` accepts `owner/repo`, `github.com/owner/repo`, a full https URL, and an `scp`-style
`git@host:owner/repo.git`, returning an https URL in the first three cases and the input unchanged for
the SSH form. Anything else throws `ArgumentException` — per the spec, unknown shapes are rejected, not
guessed.

- [ ] **Step 1: Write the local-repo test helper**

`tests/QuickRun.Core.Tests/LocalRepo.cs`:

```csharp
using QuickRun.Core.Process;

namespace QuickRun.Core.Tests;

/// A throwaway git repository on disk, used so git tests never hit the network.
public sealed class LocalRepo : IDisposable
{
    public string Path { get; }
    public string Url => new Uri(Path).AbsoluteUri;

    public LocalRepo()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "quickrun-repo-" + Guid.NewGuid().ToString("n")[..8]);
        Directory.CreateDirectory(Path);
        Git("init", "-q", "-b", "main");
        Git("config", "user.email", "test@example.com");
        Git("config", "user.name", "Test");
        Write("README.md", "hello");
        Commit("initial");
    }

    public void Write(string relativePath, string content)
    {
        var full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    public void Commit(string message)
    {
        Git("add", "-A");
        Git("commit", "-q", "-m", message);
    }

    public void Branch(string name) => Git("checkout", "-q", "-b", name);
    public void Checkout(string name) => Git("checkout", "-q", name);
    public void Tag(string name) => Git("tag", name);
    public string Head() => CommandRunner.Capture("git", new[] { "rev-parse", "HEAD" }, Path).Output.Trim();

    private void Git(params string[] args)
    {
        var r = CommandRunner.Capture("git", args, Path);
        if (r.ExitCode != 0) throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {r.Output}");
    }

    public void Dispose()
    {
        try { SetWritable(Path); Directory.Delete(Path, recursive: true); } catch { }
    }

    // git marks objects read-only on Windows, which blocks Directory.Delete
    internal static void SetWritable(string dir)
    {
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
    }
}
```

- [ ] **Step 2: Write the failing tests**

`tests/QuickRun.Core.Tests/GitClientTests.cs`:

```csharp
using QuickRun.Core.Git;
using QuickRun.Core.Process;

namespace QuickRun.Core.Tests;

public class GitClientTests
{
    private static GitClient Client(string? token = null) => new(new CredentialResolver(token, (_, _) => new CommandResult(1, "", false)));

    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "quickrun-out-" + Guid.NewGuid().ToString("n")[..8]);

    [Theory]
    [InlineData("acme/app", "https://github.com/acme/app")]
    [InlineData("github.com/acme/app", "https://github.com/acme/app")]
    [InlineData("https://github.com/acme/app", "https://github.com/acme/app")]
    [InlineData("https://github.com/acme/app.git", "https://github.com/acme/app.git")]
    [InlineData("git@github.com:acme/app.git", "git@github.com:acme/app.git")]
    public void NormalizeRepoUrl_accepts_the_documented_shapes(string input, string expected)
        => Assert.Equal(expected, GitClient.NormalizeRepoUrl(input));

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("not a url at all")]
    [InlineData("")]
    public void NormalizeRepoUrl_rejects_anything_else(string input)
        => Assert.Throws<ArgumentException>(() => GitClient.NormalizeRepoUrl(input));

    [Fact]
    public void AuthUrl_injects_a_token_into_an_https_url()
        => Assert.Equal("https://ghp_x@github.com/acme/app", GitClient.AuthUrl("https://github.com/acme/app", "ghp_x"));

    [Fact]
    public void AuthUrl_leaves_ssh_urls_and_null_tokens_alone()
    {
        Assert.Equal("git@github.com:acme/app.git", GitClient.AuthUrl("git@github.com:acme/app.git", "ghp_x"));
        Assert.Equal("https://github.com/acme/app", GitClient.AuthUrl("https://github.com/acme/app", null));
    }

    [Fact]
    public void Scrub_removes_the_token_in_plain_and_url_encoded_form()
    {
        var text = "failed for ghp_secret and ghp%5Fsecret";
        Assert.DoesNotContain("ghp_secret", GitClient.Scrub(text, "ghp_secret"));
    }

    [Fact]
    public void HostOf_extracts_the_host_from_both_url_forms()
    {
        Assert.Equal("github.com", GitClient.HostOf("https://github.com/acme/app"));
        Assert.Equal("github.com", GitClient.HostOf("git@github.com:acme/app.git"));
    }

    [Fact]
    public void CheckoutOrUpdate_clones_a_local_repository()
    {
        using var repo = new LocalRepo();
        var target = TempDir();
        try
        {
            var outcome = Client().CheckoutOrUpdate(repo.Url, "main", null, target, fresh: false);
            Assert.True(outcome.Ok, outcome.Error);
            Assert.True(File.Exists(Path.Combine(target, "README.md")));
            Assert.Equal(repo.Head(), outcome.Commit);
        }
        finally { Cleanup(target); }
    }

    [Fact]
    public void CheckoutOrUpdate_checks_out_the_requested_branch()
    {
        using var repo = new LocalRepo();
        repo.Branch("feature/login");
        repo.Write("feature.txt", "x");
        repo.Commit("feature");
        repo.Checkout("main");

        var target = TempDir();
        try
        {
            Assert.True(Client().CheckoutOrUpdate(repo.Url, "feature/login", null, target, false).Ok);
            Assert.True(File.Exists(Path.Combine(target, "feature.txt")));
        }
        finally { Cleanup(target); }
    }

    [Fact]
    public void CheckoutOrUpdate_updates_an_existing_workspace_to_the_new_head()
    {
        using var repo = new LocalRepo();
        var target = TempDir();
        try
        {
            Assert.True(Client().CheckoutOrUpdate(repo.Url, "main", null, target, false).Ok);

            repo.Write("second.txt", "x");
            repo.Commit("second");

            var outcome = Client().CheckoutOrUpdate(repo.Url, "main", null, target, false);
            Assert.True(outcome.Ok, outcome.Error);
            Assert.True(File.Exists(Path.Combine(target, "second.txt")));
            Assert.Equal(repo.Head(), outcome.Commit);
        }
        finally { Cleanup(target); }
    }

    [Fact]
    public void CheckoutOrUpdate_discards_local_modifications_on_update()
    {
        using var repo = new LocalRepo();
        var target = TempDir();
        try
        {
            Client().CheckoutOrUpdate(repo.Url, "main", null, target, false);
            File.WriteAllText(Path.Combine(target, "README.md"), "vandalised");

            Client().CheckoutOrUpdate(repo.Url, "main", null, target, false);
            Assert.Equal("hello", File.ReadAllText(Path.Combine(target, "README.md")));
        }
        finally { Cleanup(target); }
    }

    [Fact]
    public void CheckoutOrUpdate_with_fresh_true_replaces_the_directory()
    {
        using var repo = new LocalRepo();
        var target = TempDir();
        try
        {
            Client().CheckoutOrUpdate(repo.Url, "main", null, target, false);
            File.WriteAllText(Path.Combine(target, "stray.txt"), "x");

            Assert.True(Client().CheckoutOrUpdate(repo.Url, "main", null, target, fresh: true).Ok);
            Assert.False(File.Exists(Path.Combine(target, "stray.txt")));
        }
        finally { Cleanup(target); }
    }

    [Fact]
    public void CheckoutOrUpdate_reports_an_unknown_ref_as_an_error()
    {
        using var repo = new LocalRepo();
        var target = TempDir();
        try
        {
            var outcome = Client().CheckoutOrUpdate(repo.Url, "no-such-branch", null, target, false);
            Assert.False(outcome.Ok);
            Assert.NotNull(outcome.Error);
        }
        finally { Cleanup(target); }
    }

    [Fact]
    public void CheckoutOrUpdate_does_not_leak_the_token_into_the_error()
    {
        var target = TempDir();
        try
        {
            var outcome = new GitClient(new CredentialResolver("ghp_supersecret", (_, _) => new CommandResult(1, "", false)))
                .CheckoutOrUpdate("https://github.com/acme/definitely-not-real-8f2a", "main", null, target, false);
            Assert.False(outcome.Ok);
            Assert.DoesNotContain("ghp_supersecret", outcome.Error);
        }
        finally { Cleanup(target); }
    }

    [Fact]
    public void ListBranches_returns_the_branches_of_a_local_repository()
    {
        using var repo = new LocalRepo();
        repo.Branch("feature/login");
        repo.Write("f.txt", "x");
        repo.Commit("f");

        var (branches, error) = Client().ListBranches(repo.Url);
        Assert.Null(error);
        Assert.Contains("main", branches!);
        Assert.Contains("feature/login", branches!);
    }

    private static void Cleanup(string dir)
    {
        try { LocalRepo.SetWritable(dir); Directory.Delete(dir, true); } catch { }
    }
}
```

Note: these tests use `file://` URLs, which `NormalizeRepoUrl` rejects. `CheckoutOrUpdate` therefore
must **not** call `NormalizeRepoUrl` itself — normalisation is the caller's job (the CLI and the
protocol handler), and `CheckoutOrUpdate` takes whatever URL it is given. Keep that boundary; it is
what makes the git layer testable without a network.

- [ ] **Step 3: Run and watch them fail**

Run: `dotnet test tests/QuickRun.Core.Tests --filter GitClientTests`
Expected: FAIL — `GitClient` does not exist.

- [ ] **Step 4: Implement `CredentialResolver`**

The chain from the spec, first hit wins, each step guarded so a missing tool is not fatal:

```csharp
using QuickRun.Core.Process;

namespace QuickRun.Core.Git;

public sealed class CredentialResolver(string? explicitToken = null,
    Func<string, string[], CommandResult>? runner = null)
{
    private readonly Func<string, string[], CommandResult> _run =
        runner ?? ((file, args) => CommandRunner.Capture(file, args, timeoutMs: 10_000));

    public string? Resolve(string host)
    {
        if (!string.IsNullOrWhiteSpace(explicitToken)) return explicitToken;

        var fromEnv = Environment.GetEnvironmentVariable("QUICKRUN_TOKEN");
        if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv;

        var fromGh = _run("gh", new[] { "auth", "token" });
        if (fromGh.ExitCode == 0 && fromGh.Output.Trim().Length > 0) return fromGh.Output.Trim();

        return null;
    }
}
```

Steps 2 (OS credential store) and 3 (`git credential fill`) of the spec's chain are **not** in
Phase 1: the credential store is written by Phase 2's UI, and `git credential fill` needs stdin
plumbing that only pays off once a user reports the `gh` path is not enough. Plain `git clone` with no
token remains the final fallback, which is what covers SSH remotes and Git Credential Manager today.
`// ponytail: two of five credential sources; add git-credential-fill when someone hits the gap`

- [ ] **Step 5: Implement `GitClient`**

```csharp
using QuickRun.Core.Process;

namespace QuickRun.Core.Git;

public sealed record GitOutcome(bool Ok, string? Error, string? Commit);

public sealed class GitClient(CredentialResolver credentials,
    Func<string, string[], string?, CommandResult>? runner = null)
{
    private readonly Func<string, string[], string?, CommandResult> _run =
        runner ?? ((file, args, cwd) => CommandRunner.Capture(file, args, cwd, timeoutMs: 300_000));

    public GitOutcome CheckoutOrUpdate(string repoUrl, string @ref, int? pullRequest, string targetDir, bool fresh)
    {
        var token = credentials.Resolve(SafeHost(repoUrl));
        var url = AuthUrl(repoUrl, token);

        if (fresh) DeleteTree(targetDir);

        var outcome = Directory.Exists(Path.Combine(targetDir, ".git"))
            ? Update(url, @ref, pullRequest, targetDir)
            : Clone(url, @ref, pullRequest, targetDir);

        return outcome.Ok
            ? outcome with { Commit = HeadCommit(targetDir) }
            : outcome with { Error = Scrub(outcome.Error ?? "", token) };
    }
}
```

Behaviour to implement in the private members:

| Member | Behaviour |
|---|---|
| `Clone` | `git clone --depth 1 --branch <ref> <url> <dir>`; on failure with a non-`.git` URL, delete the directory and retry once with `.git` appended. For a pull request: `git clone --depth 1 <url> <dir>` then `git fetch origin pull/<n>/head` then `git checkout -q FETCH_HEAD`. |
| `Update` | `git remote set-url origin <url>`, `git fetch --depth 1 origin <ref-or-pull-spec>`, `git reset --hard FETCH_HEAD`, `git clean -fdx` with one `-e` per preserved cache. On any non-zero exit, delete the tree and fall through to `Clone` — a broken workspace should self-heal rather than block the user. |
| preserved caches | `node_modules`, `.venv`, `venv`, `obj`, `bin`, `target`, `vendor`, `.gradle`, `__pycache__` |
| `HeadCommit(dir)` | `git rev-parse HEAD`, trimmed; null when the command fails |
| `ListBranches` | `git ls-remote --heads <auth url>`, parse `refs/heads/` suffixes, distinct and sorted; retry with `.git` appended before giving up |
| `AuthUrl` | inject the token only for an `https://` URL with no existing `@`; `Uri.EscapeDataString` the token |
| `Scrub` | replace both the raw token and its URL-encoded form with `***`; a null token returns the input |
| `NormalizeRepoUrl` | `owner/repo` and `github.com/owner/repo` become `https://github.com/owner/repo`; an existing `https://` URL passes through; `git@host:owner/repo` passes through; everything else throws `ArgumentException` |
| `HostOf` | `Uri.Host` for URL forms, the segment between `@` and `:` for the SSH form |
| `SafeHost` | `HostOf` wrapped in a try/catch returning `""`, so a `file://` test URL does not throw |
| `DeleteTree` | clears read-only attributes first (git marks pack files read-only on Windows), then `Directory.Delete(dir, true)` inside a try/catch |

Every returned error string goes through `Scrub` before it leaves the class. That is the single
invariant this file exists to hold.

- [ ] **Step 6: Run and watch them pass**

Run: `dotnet test tests/QuickRun.Core.Tests --filter GitClientTests`
Expected: PASS, 18 tests.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: git checkout with workspace reuse, PR refs and token scrubbing"
```

---

### Task 10: Entry-point detection

**Files:**
- Create: `src/QuickRun.Core/Detect/Detector.cs`
- Test: `tests/QuickRun.Core.Tests/DetectorTests.cs`, `tests/QuickRun.Core.Tests/FakeRepo.cs`

**Interfaces:**
- Consumes: `OSKind` (Task 2), `ConfigParser.FindRootScript` (Task 3).
- Produces:

```csharp
public sealed record Candidate(
    string Kind,                        // "compose", "npm", "aspire", "dotnet", "python", "make", "cargo", "go", "maven", "gradle", "script"
    string Label,                       // "npm run dev (web/)"
    string RelativeDir,                 // "" for the repository root
    IReadOnlyList<string> Setup,
    IReadOnlyList<string> Run,
    int Confidence);                    // 0-100, higher sorts first

public static class Detector
{
    public static IReadOnlyList<Candidate> Detect(string root, OSKind os);
    public static string ToYaml(Candidate candidate, string? name);
}
```

Detection never executes anything. It reads files and returns commands as text, which the confirmation
dialog then shows. Directories named `.git`, `node_modules`, `bin`, `obj`, `dist`, `.venv`, `venv`,
`target`, `vendor`, `.vs`, `.idea` are skipped, and the scan stops at depth 3 so a large monorepo does
not turn into a minute of I/O.

- [ ] **Step 1: Write the fake-repo helper**

`tests/QuickRun.Core.Tests/FakeRepo.cs`:

```csharp
namespace QuickRun.Core.Tests;

/// A disposable directory tree of plain files, for detector tests.
public sealed class FakeRepo : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "quickrun-fake-" + Guid.NewGuid().ToString("n")[..8]);

    public FakeRepo() => Directory.CreateDirectory(Path);

    public FakeRepo With(string relativePath, string content = "")
    {
        var full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return this;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { }
    }
}
```

- [ ] **Step 2: Write the failing tests**

`tests/QuickRun.Core.Tests/DetectorTests.cs`:

```csharp
using QuickRun.Core;
using QuickRun.Core.Config;
using QuickRun.Core.Detect;

namespace QuickRun.Core.Tests;

public class DetectorTests
{
    private static IReadOnlyList<Candidate> Detect(FakeRepo repo) => Detector.Detect(repo.Path, OSKind.Linux);

    [Fact]
    public void An_empty_repository_yields_no_candidates()
    {
        using var repo = new FakeRepo();
        Assert.Empty(Detect(repo));
    }

    [Fact]
    public void Docker_compose_is_detected()
    {
        using var repo = new FakeRepo().With("docker-compose.yml", "services: {}");
        var c = Assert.Single(Detect(repo));
        Assert.Equal("compose", c.Kind);
        Assert.Equal(new[] { "docker compose up" }, c.Run);
        Assert.Empty(c.Setup);
    }

    [Fact]
    public void Compose_yaml_and_compose_yml_are_both_recognised()
    {
        using var repo = new FakeRepo().With("compose.yaml", "services: {}");
        Assert.Equal("compose", Assert.Single(Detect(repo)).Kind);
    }

    [Fact]
    public void An_npm_dev_script_is_detected_with_its_install_step()
    {
        using var repo = new FakeRepo().With("package.json", """{"scripts":{"dev":"vite"}}""");
        var c = Assert.Single(Detect(repo));
        Assert.Equal("npm", c.Kind);
        Assert.Equal(new[] { "npm install" }, c.Setup);
        Assert.Equal(new[] { "npm run dev" }, c.Run);
    }

    [Fact]
    public void A_lockfile_upgrades_npm_install_to_npm_ci()
    {
        using var repo = new FakeRepo()
            .With("package.json", """{"scripts":{"dev":"vite"}}""")
            .With("package-lock.json", "{}");
        Assert.Equal(new[] { "npm ci" }, Assert.Single(Detect(repo)).Setup);
    }

    [Fact]
    public void A_pnpm_lockfile_switches_the_package_manager()
    {
        using var repo = new FakeRepo()
            .With("package.json", """{"scripts":{"dev":"vite"}}""")
            .With("pnpm-lock.yaml", "");
        var c = Assert.Single(Detect(repo));
        Assert.Equal(new[] { "pnpm install" }, c.Setup);
        Assert.Equal(new[] { "pnpm run dev" }, c.Run);
    }

    [Fact]
    public void Start_is_used_when_there_is_no_dev_script()
    {
        using var repo = new FakeRepo().With("package.json", """{"scripts":{"start":"node ."}}""");
        Assert.Equal(new[] { "npm run start" }, Assert.Single(Detect(repo)).Run);
    }

    [Fact]
    public void A_package_json_without_dev_or_start_yields_nothing()
    {
        using var repo = new FakeRepo().With("package.json", """{"scripts":{"build":"tsc"}}""");
        Assert.Empty(Detect(repo));
    }

    [Fact]
    public void Malformed_package_json_does_not_throw()
    {
        using var repo = new FakeRepo().With("package.json", "{not json");
        Assert.Empty(Detect(repo));
    }

    [Fact]
    public void An_aspire_apphost_outranks_a_plain_csproj()
    {
        using var repo = new FakeRepo()
            .With("src/AppHost/AppHost.csproj", "<Project Sdk=\"Aspire.AppHost.Sdk\"></Project>")
            .With("src/Api/Api.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\"></Project>");
        var first = Detect(repo)[0];
        Assert.Equal("aspire", first.Kind);
        Assert.Contains("AppHost.csproj", first.Run[0]);
    }

    [Fact]
    public void A_web_csproj_is_detected_as_a_dotnet_candidate()
    {
        using var repo = new FakeRepo().With("src/Api/Api.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\"></Project>");
        var c = Assert.Single(Detect(repo));
        Assert.Equal("dotnet", c.Kind);
        Assert.Contains("dotnet run --project", c.Run[0]);
    }

    [Fact]
    public void A_library_csproj_is_not_a_candidate()
    {
        using var repo = new FakeRepo().With("src/Lib/Lib.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        Assert.Empty(Detect(repo));
    }

    [Fact]
    public void Requirements_txt_with_main_py_produces_a_venv_flow()
    {
        using var repo = new FakeRepo().With("requirements.txt", "flask").With("main.py", "");
        var c = Assert.Single(Detect(repo));
        Assert.Equal("python", c.Kind);
        Assert.Contains("venv", c.Setup[0]);
        Assert.Contains("requirements.txt", string.Join(" ", c.Setup));
        Assert.Contains("main.py", c.Run[0]);
    }

    [Fact]
    public void A_makefile_run_target_is_detected()
    {
        using var repo = new FakeRepo().With("Makefile", "build:\n\tgcc x.c\nrun:\n\t./a.out\n");
        var c = Assert.Single(Detect(repo));
        Assert.Equal("make", c.Kind);
        Assert.Equal(new[] { "make run" }, c.Run);
    }

    [Fact]
    public void A_makefile_without_run_or_dev_targets_yields_nothing()
    {
        using var repo = new FakeRepo().With("Makefile", "build:\n\tgcc x.c\n");
        Assert.Empty(Detect(repo));
    }

    [Fact]
    public void Cargo_and_go_projects_are_detected()
    {
        using (var repo = new FakeRepo().With("Cargo.toml", "[package]"))
            Assert.Equal(new[] { "cargo run" }, Assert.Single(Detect(repo)).Run);
        using (var repo = new FakeRepo().With("go.mod", "module x"))
            Assert.Equal("go", Assert.Single(Detect(repo)).Kind);
    }

    [Fact]
    public void A_root_run_script_is_the_highest_ranked_candidate()
    {
        using var repo = new FakeRepo().With("run.sh", "#!/bin/sh\necho hi").With("docker-compose.yml", "services: {}");
        var candidates = Detect(repo);
        Assert.Equal("script", candidates[0].Kind);
        Assert.Equal(new[] { "./run.sh" }, candidates[0].Run);
    }

    [Fact]
    public void A_monorepo_yields_one_candidate_per_directory()
    {
        using var repo = new FakeRepo()
            .With("web/package.json", """{"scripts":{"dev":"vite"}}""")
            .With("api/package.json", """{"scripts":{"dev":"nest start"}}""");
        var candidates = Detect(repo);
        Assert.Equal(2, candidates.Count);
        Assert.Contains(candidates, c => c.RelativeDir == "web");
        Assert.Contains(candidates, c => c.RelativeDir == "api");
    }

    [Fact]
    public void Ignored_directories_are_not_scanned()
    {
        using var repo = new FakeRepo().With("node_modules/pkg/package.json", """{"scripts":{"dev":"x"}}""");
        Assert.Empty(Detect(repo));
    }

    [Fact]
    public void Candidates_are_ordered_by_descending_confidence()
    {
        using var repo = new FakeRepo()
            .With("docker-compose.yml", "services: {}")
            .With("package.json", """{"scripts":{"dev":"vite"}}""");
        var candidates = Detect(repo);
        Assert.True(candidates[0].Confidence >= candidates[1].Confidence);
    }

    [Fact]
    public void ToYaml_produces_a_config_that_the_parser_accepts()
    {
        using var repo = new FakeRepo()
            .With("package.json", """{"scripts":{"dev":"vite"}}""")
            .With("package-lock.json", "{}");
        var yaml = Detector.ToYaml(Assert.Single(Detect(repo)), "web-app");

        var parsed = ConfigParser.Parse(yaml, OSKind.Linux);
        Assert.Equal("web-app", parsed.Name);
        Assert.Equal(new[] { "npm ci" }, parsed.Setup.Select(s => s.Run));
        Assert.Equal("npm run dev", Assert.Single(parsed.Tasks).Run);
    }

    [Fact]
    public void ToYaml_emits_cwd_for_a_candidate_in_a_subdirectory()
    {
        using var repo = new FakeRepo().With("web/package.json", """{"scripts":{"dev":"vite"}}""");
        var parsed = ConfigParser.Parse(Detector.ToYaml(Assert.Single(Detect(repo)), null), OSKind.Linux);
        Assert.Equal("web", parsed.Tasks[0].Cwd);
    }
}
```

- [ ] **Step 3: Run and watch them fail**

Run: `dotnet test tests/QuickRun.Core.Tests --filter DetectorTests`
Expected: FAIL — `Detector` does not exist.

- [ ] **Step 4: Implement `Detector`**

Structure: one private method per signal, each returning `Candidate?` or a sequence, all invoked from
`Detect` over the walked directory list. Confidence values, highest first:

| Kind | Confidence | Trigger | Setup | Run |
|---|---|---|---|---|
| `script` | 95 | `ConfigParser.FindRootScript` hits | — | the script path (`./run.sh`, or `powershell -File ./run.ps1` on Windows) |
| `compose` | 90 | `docker-compose.y[a]ml` or `compose.y[a]ml` | — | `docker compose up` |
| `aspire` | 85 | a `.csproj` containing `Aspire.AppHost.Sdk` or `<IsAspireHost>true` | — | `dotnet run --project <rel>` |
| `npm` | 80 | `package.json` with a `dev` or `start` script | `<pm> install` or `<pm> ci` | `<pm> run <script>` |
| `python` | 75 | `requirements.txt` or `pyproject.toml`, plus `main.py`/`app.py`/`manage.py` | see below | `<venv-python> <file>` |
| `dotnet` | 70 | a `.csproj` whose SDK is `Microsoft.NET.Sdk.Web` or which contains `<OutputType>Exe` | — | `dotnet run --project <rel>` |
| `make` | 65 | `Makefile` with a `run:` or `dev:` target | — | `make <target>` |
| `cargo` | 60 | `Cargo.toml` | — | `cargo run` |
| `go` | 60 | `go.mod` | — | `go run ./...` |
| `maven` | 55 | `pom.xml` | — | `mvn spring-boot:run` |
| `gradle` | 55 | `build.gradle[.kts]` | — | `./gradlew bootRun` |

Package-manager selection for `npm`: `pnpm-lock.yaml` gives `pnpm`, `yarn.lock` gives `yarn`,
`bun.lockb` gives `bun`, otherwise `npm`. The install command is `ci` only for npm with a
`package-lock.json`; `pnpm`/`yarn`/`bun` always use `install`.

Python setup, platform-dependent because the venv layout differs:

```
linux/macos:  python3 -m venv .venv           windows: python -m venv .venv
              .venv/bin/pip install -r requirements.txt   .venv\Scripts\pip install -r requirements.txt
run:          .venv/bin/python main.py         .venv\Scripts\python main.py
```

For `pyproject.toml` without `requirements.txt`, use `uv sync` + `uv run <file>` when a `uv.lock`
exists, otherwise `poetry install` + `poetry run python <file>`.

`ToYaml` writes the config by hand rather than through a serialiser, so the output is the shape a
human would have written:

```csharp
public static string ToYaml(Candidate c, string? name)
{
    var sb = new StringBuilder();
    sb.AppendLine("# yaml-language-server: $schema=https://fgilde.github.io/QuickRun/quickrun.schema.json");
    sb.AppendLine("version: 1");
    if (!string.IsNullOrWhiteSpace(name)) sb.AppendLine($"name: {name}");
    sb.AppendLine($"# generated by quickrun from a detected {c.Kind} entry point - review before committing");

    if (c.Setup.Count > 0)
    {
        sb.AppendLine("setup:");
        foreach (var s in c.Setup) sb.AppendLine($"  - run: {Quote(s)}");
        if (c.RelativeDir.Length > 0)
            sb.AppendLine($"    # cwd is repeated per step because setup steps do not inherit it");
    }

    sb.AppendLine("tasks:");
    for (var i = 0; i < c.Run.Count; i++)
    {
        sb.AppendLine($"  - name: {(c.Run.Count == 1 ? "run" : $"task-{i + 1}")}");
        sb.AppendLine($"    run: {Quote(c.Run[i])}");
        if (c.RelativeDir.Length > 0) sb.AppendLine($"    cwd: {c.RelativeDir}");
    }
    return sb.ToString();
}
```

Correction to the snippet above: setup steps in a subdirectory need their own `cwd`, so emit
`    cwd: <dir>` after each setup step instead of the comment. `Quote` wraps a value in double quotes
only when it contains a character YAML would treat specially (`:`, `#`, `{`, `}`, `[`, `]`, `,`, `&`,
`*`, `!`, `|`, `>`, `%`, `@`, a leading `-`, or leading/trailing whitespace).

- [ ] **Step 5: Run and watch them pass**

Run: `dotnet test tests/QuickRun.Core.Tests --filter DetectorTests`
Expected: PASS, 22 tests.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: entry-point detection with config generation"
```

---

### Task 11: Readiness probes

**Files:**
- Create: `src/QuickRun.Core/Run/Readiness.cs`
- Test: `tests/QuickRun.Core.Tests/ReadinessTests.cs`

**Interfaces:**
- Consumes: `ReadyWhen` (Task 3).
- Produces:

```csharp
public static class Readiness
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(3);

    public static Task<bool> WaitAsync(
        ReadyWhen? readyWhen,
        Func<string> logSoFar,
        TimeSpan timeout,
        CancellationToken ct,
        Func<int, Task<bool>>? portProbe = null,
        Func<string, Task<bool>>? httpProbe = null);
}
```

A null `readyWhen` returns true immediately. The two probe delegates default to a real TCP connect and
a real HTTP `GET`, and exist so the tests do not need to bind ports or start a web server.

- [ ] **Step 1: Write the failing tests**

`tests/QuickRun.Core.Tests/ReadinessTests.cs`:

```csharp
using QuickRun.Core.Config;
using QuickRun.Core.Run;

namespace QuickRun.Core.Tests;

public class ReadinessTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(600);

    [Fact]
    public async Task No_readiness_condition_is_immediately_ready()
        => Assert.True(await Readiness.WaitAsync(null, () => "", Short, CancellationToken.None));

    [Fact]
    public async Task A_delay_condition_waits_and_then_succeeds()
    {
        var rw = new ReadyWhen(null, null, null, TimeSpan.FromMilliseconds(50));
        Assert.True(await Readiness.WaitAsync(rw, () => "", Short, CancellationToken.None));
    }

    [Fact]
    public async Task A_port_that_opens_on_the_third_attempt_succeeds()
    {
        var attempts = 0;
        var rw = new ReadyWhen(5000, null, null, null);
        var ok = await Readiness.WaitAsync(rw, () => "", TimeSpan.FromSeconds(5), CancellationToken.None,
            portProbe: _ => Task.FromResult(++attempts >= 3));
        Assert.True(ok);
        Assert.True(attempts >= 3);
    }

    [Fact]
    public async Task A_port_that_never_opens_times_out()
    {
        var rw = new ReadyWhen(5000, null, null, null);
        Assert.False(await Readiness.WaitAsync(rw, () => "", Short, CancellationToken.None,
            portProbe: _ => Task.FromResult(false)));
    }

    [Fact]
    public async Task An_http_probe_that_succeeds_is_ready()
    {
        var rw = new ReadyWhen(null, "http://localhost:1/", null, null);
        Assert.True(await Readiness.WaitAsync(rw, () => "", Short, CancellationToken.None,
            httpProbe: _ => Task.FromResult(true)));
    }

    [Fact]
    public async Task A_log_pattern_that_appears_is_ready()
    {
        var log = "";
        var rw = new ReadyWhen(null, null, "Now listening on: (?<url>\\S+)", null);
        var task = Readiness.WaitAsync(rw, () => log, TimeSpan.FromSeconds(5), CancellationToken.None);
        await Task.Delay(120);
        log = "info: Now listening on: http://localhost:5000";
        Assert.True(await task);
    }

    [Fact]
    public async Task A_log_pattern_that_never_appears_times_out()
    {
        var rw = new ReadyWhen(null, null, "never-appears", null);
        Assert.False(await Readiness.WaitAsync(rw, () => "nothing here", Short, CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_stops_waiting_and_reports_not_ready()
    {
        using var cts = new CancellationTokenSource(100);
        var rw = new ReadyWhen(5000, null, null, null);
        Assert.False(await Readiness.WaitAsync(rw, () => "", TimeSpan.FromMinutes(1), cts.Token,
            portProbe: _ => Task.FromResult(false)));
    }

    [Fact]
    public async Task An_invalid_log_regex_reports_not_ready_instead_of_throwing()
    {
        var rw = new ReadyWhen(null, null, "[unclosed", null);
        Assert.False(await Readiness.WaitAsync(rw, () => "anything", Short, CancellationToken.None));
    }
}
```

- [ ] **Step 2: Run and watch them fail**

Run: `dotnet test tests/QuickRun.Core.Tests --filter ReadinessTests`
Expected: FAIL — `Readiness` does not exist.

- [ ] **Step 3: Implement `Readiness`**

```csharp
using System.Net.Sockets;
using System.Text.RegularExpressions;
using QuickRun.Core.Config;

namespace QuickRun.Core.Run;

public static class Readiness
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    public static async Task<bool> WaitAsync(ReadyWhen? readyWhen, Func<string> logSoFar,
        TimeSpan timeout, CancellationToken ct,
        Func<int, Task<bool>>? portProbe = null, Func<string, Task<bool>>? httpProbe = null)
    {
        if (readyWhen is null) return true;

        if (readyWhen.Delay is { } delay)
        {
            try { await Task.Delay(delay, ct); return true; }
            catch (OperationCanceledException) { return false; }
        }

        portProbe ??= PortOpenAsync;
        httpProbe ??= HttpOkAsync;
        Regex? logPattern = null;
        if (readyWhen.Log is { } pattern)
        {
            try { logPattern = new Regex(pattern, RegexOptions.Compiled); }
            catch (ArgumentException) { return false; }
        }

        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var ready = readyWhen switch
            {
                { Port: { } port } => await Safe(() => portProbe(port)),
                { Http: { } url } => await Safe(() => httpProbe(url)),
                _ when logPattern is not null => logPattern.IsMatch(logSoFar()),
                _ => true,
            };
            if (ready) return true;

            try { await Task.Delay(PollInterval, ct); }
            catch (OperationCanceledException) { return false; }
        }
        return false;
    }

    private static async Task<bool> Safe(Func<Task<bool>> probe)
    {
        try { return await probe(); } catch { return false; }
    }

    private static async Task<bool> PortOpenAsync(int port)
    {
        using var client = new TcpClient();
        var connect = client.ConnectAsync("127.0.0.1", port);
        var finished = await Task.WhenAny(connect, Task.Delay(500));
        return finished == connect && client.Connected;
    }

    private static async Task<bool> HttpOkAsync(string url)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        return (int)response.StatusCode < 500;
    }
}
```

`HttpOkAsync` accepts anything below 500 on purpose: a dev server answering 404 on `/` is up, and
waiting for a 200 would hang on apps whose root path is not routed.

- [ ] **Step 4: Run and watch them pass**

Run: `dotnet test tests/QuickRun.Core.Tests --filter ReadinessTests`
Expected: PASS, 9 tests.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: readiness probes for port, http, log and delay"
```

---

### Task 12: Run plan — the auditable command list

**Files:**
- Create: `src/QuickRun.Core/Run/RunPlan.cs`
- Test: `tests/QuickRun.Core.Tests/RunPlanTests.cs`

**Interfaces:**
- Consumes: `RunConfig`, `Step`, `TaskDef` (Task 3), `Interpolator`, `InterpolationContext` (Task 5), `OSKind` (Task 2).
- Produces:

```csharp
public sealed record PlannedCommand(string Phase, string Name, string Command, string? Cwd);

public sealed record RunPlan(
    string Repo, string Ref, string? Commit, string Workspace, string DisplayName,
    IReadOnlyList<PlannedCommand> Commands, IReadOnlyList<ToolRequirement> Requires,
    IReadOnlyList<InputDef> Inputs)
{
    public string Fingerprint { get; }   // stable SHA-256 hex over the command list
    public string Describe();            // multi-line text for the confirmation prompt
}

public static class RunPlanBuilder
{
    public static RunPlan Build(RunConfig config, InterpolationContext ctx, OSKind os,
        string repo, string @ref, string? commit);
}
```

This is the type the security model rests on: it is what the CLI prints before asking for
confirmation, what Phase 2's dialog renders, and what the trust store hashes. `Phase` is `"setup"`,
`"task"` or `"stop"`. Steps whose `When` excludes the current platform are dropped, so the list shows
exactly what will execute on this machine and nothing else.

`Fingerprint` deliberately covers only phase, name, command and cwd — not the repo, ref or commit.
That is what makes "trust this repo" survive new commits but break when the commands change.

- [ ] **Step 1: Write the failing tests**

`tests/QuickRun.Core.Tests/RunPlanTests.cs`:

```csharp
using QuickRun.Core;
using QuickRun.Core.Config;
using QuickRun.Core.Run;

namespace QuickRun.Core.Tests;

public class RunPlanTests
{
    private static InterpolationContext Ctx(params (string, string?)[] inputs) =>
        new(inputs.ToDictionary(i => i.Item1, i => i.Item2), "/w/acme__app__main", "app", "main",
            EnvLookup: _ => null);

    private static RunPlan Plan(string yaml, OSKind os = OSKind.Linux, params (string, string?)[] inputs) =>
        RunPlanBuilder.Build(ConfigParser.Parse(yaml, os), Ctx(inputs), os,
            "https://github.com/acme/app", "main", "abc1234");

    [Fact]
    public void A_single_run_command_becomes_one_planned_task()
    {
        var plan = Plan("run: ./run.sh");
        var c = Assert.Single(plan.Commands);
        Assert.Equal("task", c.Phase);
        Assert.Equal("run", c.Name);
        Assert.Equal("./run.sh", c.Command);
    }

    [Fact]
    public void Setup_tasks_and_stop_appear_in_that_order()
    {
        var plan = Plan("setup: [npm ci]\ntasks: [npm start]\nstop: [docker compose down]");
        Assert.Equal(new[] { "setup", "task", "stop" }, plan.Commands.Select(c => c.Phase));
    }

    [Fact]
    public void Placeholders_are_expanded_in_the_plan()
    {
        var plan = Plan("inputs:\n  - id: apiKey\nrun: ./app --key ${inputs.apiKey}", inputs: ("apiKey", "sk-1"));
        Assert.Equal("./app --key sk-1", plan.Commands[0].Command);
    }

    [Fact]
    public void Workspace_placeholder_is_expanded()
        => Assert.Equal("ls /w/acme__app__main", Plan("run: ls ${workspace}").Commands[0].Command);

    [Fact]
    public void Steps_excluded_by_when_are_not_in_the_plan()
    {
        var yaml = string.Join("\n",
            "setup:",
            "  - run: apt-get install -y libfoo",
            "    when: linux",
            "  - run: brew install foo",
            "    when: macos",
            "run: ./app");
        Assert.Equal(new[] { "apt-get install -y libfoo", "./app" },
            Plan(yaml, OSKind.Linux).Commands.Select(c => c.Command));
        Assert.Equal(new[] { "brew install foo", "./app" },
            Plan(yaml, OSKind.MacOs).Commands.Select(c => c.Command));
    }

    [Fact]
    public void Cwd_is_carried_into_the_plan()
        => Assert.Equal("web", Plan("tasks:\n  - run: npm run dev\n    cwd: web").Commands[0].Cwd);

    [Fact]
    public void DisplayName_falls_back_to_the_repository_name()
    {
        Assert.Equal("app", Plan("run: ./a").DisplayName);
        Assert.Equal("My App", Plan("name: My App\nrun: ./a").DisplayName);
    }

    [Fact]
    public void The_fingerprint_is_stable_for_the_same_commands()
        => Assert.Equal(Plan("run: ./run.sh").Fingerprint, Plan("run: ./run.sh").Fingerprint);

    [Fact]
    public void The_fingerprint_changes_when_a_command_changes()
        => Assert.NotEqual(Plan("run: ./run.sh").Fingerprint, Plan("run: ./evil.sh").Fingerprint);

    [Fact]
    public void The_fingerprint_changes_when_a_setup_step_is_added()
        => Assert.NotEqual(Plan("run: ./run.sh").Fingerprint, Plan("setup: [curl x]\nrun: ./run.sh").Fingerprint);

    [Fact]
    public void The_fingerprint_ignores_the_commit()
    {
        var a = RunPlanBuilder.Build(ConfigParser.Parse("run: ./run.sh", OSKind.Linux), Ctx(), OSKind.Linux,
            "https://github.com/acme/app", "main", "aaaaaaa");
        var b = RunPlanBuilder.Build(ConfigParser.Parse("run: ./run.sh", OSKind.Linux), Ctx(), OSKind.Linux,
            "https://github.com/acme/app", "main", "bbbbbbb");
        Assert.Equal(a.Fingerprint, b.Fingerprint);
    }

    [Fact]
    public void The_fingerprint_differs_between_platforms_when_the_commands_differ()
    {
        var yaml = "run:\n  linux: ./run.sh\n  windows: ./run.ps1\n  macos: ./run.sh";
        Assert.NotEqual(Plan(yaml, OSKind.Linux).Fingerprint, Plan(yaml, OSKind.Windows).Fingerprint);
    }

    [Fact]
    public void Describe_names_the_repository_the_ref_the_commit_and_every_command()
    {
        var text = Plan("setup: [npm ci]\ntasks: [npm start]").Describe();
        Assert.Contains("https://github.com/acme/app", text);
        Assert.Contains("main", text);
        Assert.Contains("abc1234", text);
        Assert.Contains("npm ci", text);
        Assert.Contains("npm start", text);
    }

    [Fact]
    public void Requires_and_inputs_travel_with_the_plan()
    {
        var plan = Plan("requires:\n  - tool: node\n    version: \">=20\"\ninputs:\n  - id: k\nrun: ./a");
        Assert.Equal("node", Assert.Single(plan.Requires).Tool);
        Assert.Equal("k", Assert.Single(plan.Inputs).Id);
    }
}
```

- [ ] **Step 2: Run and watch them fail**

Run: `dotnet test tests/QuickRun.Core.Tests --filter RunPlanTests`
Expected: FAIL — `RunPlan` does not exist.

- [ ] **Step 3: Implement `RunPlan` and `RunPlanBuilder`**

```csharp
using System.Security.Cryptography;
using System.Text;
using QuickRun.Core.Config;

namespace QuickRun.Core.Run;

public sealed record PlannedCommand(string Phase, string Name, string Command, string? Cwd);

public sealed record RunPlan(string Repo, string Ref, string? Commit, string Workspace, string DisplayName,
    IReadOnlyList<PlannedCommand> Commands, IReadOnlyList<ToolRequirement> Requires,
    IReadOnlyList<InputDef> Inputs)
{
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("\n", Commands.Select(c => $"{c.Phase}\t{c.Name}\t{c.Command}\t{c.Cwd}"))))).ToLowerInvariant();

    public string Describe()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{DisplayName}");
        sb.AppendLine($"  repository  {Repo}");
        sb.AppendLine($"  ref         {Ref}{(Commit is null ? "" : $" ({Commit[..Math.Min(7, Commit.Length)]})")}");
        sb.AppendLine($"  workspace   {Workspace}");
        sb.AppendLine();
        sb.AppendLine("These commands will run:");
        foreach (var group in Commands.GroupBy(c => c.Phase))
        {
            sb.AppendLine($"  {group.Key}:");
            foreach (var c in group)
                sb.AppendLine($"    {c.Command}{(string.IsNullOrEmpty(c.Cwd) ? "" : $"   (in {c.Cwd})")}");
        }
        return sb.ToString();
    }
}

public static class RunPlanBuilder
{
    public static RunPlan Build(RunConfig config, InterpolationContext ctx, OSKind os,
        string repo, string @ref, string? commit)
    {
        var platform = os.Key();
        var commands = new List<PlannedCommand>();

        void AddSteps(IReadOnlyList<Step> steps, string phase)
        {
            var i = 0;
            foreach (var s in steps)
            {
                i++;
                if (s.When.Count > 0 && !s.When.Contains(platform, StringComparer.OrdinalIgnoreCase)) continue;
                commands.Add(new(phase, $"{phase}-{i}", Interpolator.Expand(s.Run, ctx), Expand(s.Cwd, ctx)));
            }
        }

        AddSteps(config.Setup, "setup");
        foreach (var t in config.Tasks)
            commands.Add(new("task", t.Name, Interpolator.Expand(t.Run, ctx), Expand(t.Cwd, ctx)));
        AddSteps(config.Stop, "stop");

        return new RunPlan(repo, @ref, commit, ctx.Workspace,
            config.Name ?? ctx.RepoName, commands, config.Requires, config.Inputs);
    }

    private static string? Expand(string? value, InterpolationContext ctx) =>
        value is null ? null : Interpolator.Expand(value, ctx);
}
```

- [ ] **Step 4: Run and watch them pass**

Run: `dotnet test tests/QuickRun.Core.Tests --filter RunPlanTests`
Expected: PASS, 14 tests.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: run plan with stable command fingerprint for trust decisions"
```

---

### Task 13: Runner — supervising the processes

**Files:**
- Create: `src/QuickRun.Core/Run/Runner.cs`
- Test: `tests/QuickRun.Core.Tests/RunnerTests.cs`

**Interfaces:**
- Consumes: `CommandRunner`, `ProcessSpec` (Task 2), `RunConfig`/`TaskDef`/`Step` (Task 3), `Interpolator` (Task 5), `ToolChecker` (Task 6), `Readiness` (Task 11).
- Produces:

```csharp
public enum RunEventKind { Info, Output, Error, TaskStarted, TaskReady, TaskExited, Failed, Finished }

public sealed record RunEvent(RunEventKind Kind, string? Task, string Text);

public sealed record RunOutcome(bool Ok, string? Error);

public sealed record RunOptions(
    string Workspace,
    InterpolationContext Context,
    IReadOnlyDictionary<string, string> ExtraEnv,
    IReadOnlyList<string> Secrets,
    TimeSpan ReadyTimeout,
    bool SkipRequires = false);

public sealed class Runner : IAsyncDisposable
{
    public Runner(Action<RunEvent> onEvent);
    public Task<RunOutcome> ExecuteAsync(RunConfig config, RunOptions options, CancellationToken ct);
    public Task StopAsync();
}
```

Every string that reaches `onEvent` passes through `Interpolator.Redact(text, options.Secrets)` first.
That is the single place where secret leakage is prevented, so it must not be bypassed anywhere else in
the class.

Lifecycle inside `ExecuteAsync`:

1. `ToolChecker.CheckAll(config.Requires)` unless `SkipRequires`; any result where `Blocks` is true
   fails the run with a message listing each blocker and its `install` hint.
2. `setup` steps in order, skipping platform-excluded ones, each awaited to completion. A non-zero exit
   fails the run unless the step sets `ContinueOnError`.
3. `tasks` started concurrently, except that a task waits for every entry in its `DependsOn` to have
   signalled `TaskReady`.
4. When a task's `OpenReady`/`OpenUrl` applies and it becomes ready, raise an `Info` event carrying the
   URL. Core does **not** launch a browser — that is the CLI's and the UI's decision.
5. `ExecuteAsync` returns once every task has exited, or immediately on cancellation.
6. `StopAsync` cancels the tasks, waits briefly for them, then runs the `stop` steps.

- [ ] **Step 1: Write the failing tests**

`tests/QuickRun.Core.Tests/RunnerTests.cs`:

```csharp
using QuickRun.Core;
using QuickRun.Core.Config;
using QuickRun.Core.Run;

namespace QuickRun.Core.Tests;

public class RunnerTests
{
    private static RunOptions Options(string workspace, params string[] secrets) =>
        new(workspace,
            new InterpolationContext(new Dictionary<string, string?>(), workspace, "app", "main", _ => null),
            new Dictionary<string, string>(), secrets, TimeSpan.FromSeconds(5), SkipRequires: true);

    private static RunConfig Config(string yaml) => ConfigParser.Parse(yaml, OSKinds.Current);

    private sealed class Recorder
    {
        public List<RunEvent> Events { get; } = new();
        public Action<RunEvent> Sink => e => { lock (Events) Events.Add(e); };
        public string Text => string.Join("\n", Events.Select(e => e.Text));
    }

    [Fact]
    public async Task A_setup_step_runs_and_its_output_is_reported()
    {
        using var repo = new FakeRepo();
        var log = new Recorder();
        await using var runner = new Runner(log.Sink);

        var outcome = await runner.ExecuteAsync(Config("setup: [echo setup-ran]\ntasks: []"),
            Options(repo.Path), CancellationToken.None);

        Assert.True(outcome.Ok, outcome.Error);
        Assert.Contains("setup-ran", log.Text);
    }

    [Fact]
    public async Task A_failing_setup_step_fails_the_run()
    {
        using var repo = new FakeRepo();
        await using var runner = new Runner(_ => { });
        var outcome = await runner.ExecuteAsync(Config("setup: [exit 4]\ntasks: []"),
            Options(repo.Path), CancellationToken.None);
        Assert.False(outcome.Ok);
        Assert.Contains("setup", outcome.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ContinueOnError_lets_the_run_proceed_past_a_failing_step()
    {
        using var repo = new FakeRepo();
        var log = new Recorder();
        await using var runner = new Runner(log.Sink);

        var yaml = string.Join("\n",
            "setup:",
            "  - run: exit 4",
            "    continueOnError: true",
            "  - run: echo second-step",
            "tasks: []");
        var outcome = await runner.ExecuteAsync(Config(yaml), Options(repo.Path), CancellationToken.None);

        Assert.True(outcome.Ok, outcome.Error);
        Assert.Contains("second-step", log.Text);
    }

    [Fact]
    public async Task A_task_runs_to_completion_and_reports_its_exit()
    {
        using var repo = new FakeRepo();
        var log = new Recorder();
        await using var runner = new Runner(log.Sink);

        var outcome = await runner.ExecuteAsync(Config("tasks:\n  - name: hello\n    run: echo task-ran"),
            Options(repo.Path), CancellationToken.None);

        Assert.True(outcome.Ok, outcome.Error);
        Assert.Contains("task-ran", log.Text);
        Assert.Contains(log.Events, e => e.Kind == RunEventKind.TaskExited && e.Task == "hello");
    }

    [Fact]
    public async Task Steps_run_in_the_workspace_directory()
    {
        using var repo = new FakeRepo().With("marker.txt", "x");
        var log = new Recorder();
        await using var runner = new Runner(log.Sink);

        var command = OSKinds.Current == OSKind.Windows ? "dir /b" : "ls";
        await runner.ExecuteAsync(Config($"tasks:\n  - run: {command}"), Options(repo.Path), CancellationToken.None);

        Assert.Contains("marker.txt", log.Text);
    }

    [Fact]
    public async Task Cwd_is_resolved_relative_to_the_workspace()
    {
        using var repo = new FakeRepo().With("web/inner.txt", "x");
        var log = new Recorder();
        await using var runner = new Runner(log.Sink);

        var command = OSKinds.Current == OSKind.Windows ? "dir /b" : "ls";
        await runner.ExecuteAsync(Config($"tasks:\n  - run: {command}\n    cwd: web"),
            Options(repo.Path), CancellationToken.None);

        Assert.Contains("inner.txt", log.Text);
    }

    [Fact]
    public async Task Secrets_are_redacted_from_reported_output()
    {
        using var repo = new FakeRepo();
        var log = new Recorder();
        await using var runner = new Runner(log.Sink);

        await runner.ExecuteAsync(Config("tasks:\n  - run: echo sk-supersecret"),
            Options(repo.Path, "sk-supersecret"), CancellationToken.None);

        Assert.DoesNotContain("sk-supersecret", log.Text);
        Assert.Contains("***", log.Text);
    }

    [Fact]
    public async Task Extra_environment_variables_reach_the_child_process()
    {
        using var repo = new FakeRepo();
        var log = new Recorder();
        await using var runner = new Runner(log.Sink);

        var options = Options(repo.Path) with
        {
            ExtraEnv = new Dictionary<string, string> { ["QUICKRUN_TEST_TOKEN"] = "abc123" }
        };
        var command = OSKinds.Current == OSKind.Windows ? "echo %QUICKRUN_TEST_TOKEN%" : "echo $QUICKRUN_TEST_TOKEN";
        await runner.ExecuteAsync(Config($"tasks:\n  - run: {command}"), options, CancellationToken.None);

        Assert.Contains("abc123", log.Text);
    }

    [Fact]
    public async Task A_dependent_task_starts_only_after_its_dependency_is_ready()
    {
        using var repo = new FakeRepo();
        var order = new List<string>();
        await using var runner = new Runner(e =>
        {
            if (e.Kind == RunEventKind.TaskStarted) lock (order) order.Add(e.Task!);
        });

        var yaml = string.Join("\n",
            "tasks:",
            "  - name: first",
            "    run: echo one",
            "    readyWhen: {delay: 200ms}",
            "  - name: second",
            "    run: echo two",
            "    dependsOn: [first]");
        await runner.ExecuteAsync(Config(yaml), Options(repo.Path), CancellationToken.None);

        Assert.Equal(new[] { "first", "second" }, order);
    }

    [Fact]
    public async Task A_blocking_requirement_fails_the_run_before_anything_executes()
    {
        using var repo = new FakeRepo();
        var log = new Recorder();
        await using var runner = new Runner(log.Sink);

        var options = Options(repo.Path) with { SkipRequires = false };
        var yaml = string.Join("\n",
            "requires:",
            "  - tool: definitely-not-installed-9f2a",
            "    install: https://example.com/get",
            "setup: [echo should-not-run]",
            "tasks: []");
        var outcome = await runner.ExecuteAsync(Config(yaml), options, CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.Contains("definitely-not-installed-9f2a", outcome.Error!);
        Assert.Contains("https://example.com/get", outcome.Error!);
        Assert.DoesNotContain("should-not-run", log.Text);
    }

    [Fact]
    public async Task StopAsync_runs_the_stop_steps()
    {
        using var repo = new FakeRepo();
        var log = new Recorder();
        await using var runner = new Runner(log.Sink);

        var sleep = OSKinds.Current == OSKind.Windows ? "ping -n 30 127.0.0.1 >nul" : "sleep 30";
        var yaml = $"tasks:\n  - name: long\n    run: {sleep}\nstop: [echo stopped]";

        var run = runner.ExecuteAsync(Config(yaml), Options(repo.Path), CancellationToken.None);
        await Task.Delay(300);
        await runner.StopAsync();
        await run;

        Assert.Contains("stopped", log.Text);
    }

    [Fact]
    public async Task Cancellation_terminates_a_long_running_task()
    {
        using var repo = new FakeRepo();
        using var cts = new CancellationTokenSource();
        await using var runner = new Runner(_ => { });

        var sleep = OSKinds.Current == OSKind.Windows ? "ping -n 60 127.0.0.1 >nul" : "sleep 60";
        var run = runner.ExecuteAsync(Config($"tasks:\n  - run: {sleep}"), Options(repo.Path), cts.Token);
        await Task.Delay(300);
        cts.Cancel();

        var outcome = await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(outcome.Ok);
    }

    [Fact]
    public async Task An_open_url_is_reported_as_an_info_event_and_not_launched()
    {
        using var repo = new FakeRepo();
        var log = new Recorder();
        await using var runner = new Runner(log.Sink);

        var yaml = string.Join("\n",
            "tasks:",
            "  - name: web",
            "    run: echo up",
            "    readyWhen: {delay: 50ms}",
            "    open: http://localhost:5173");
        await runner.ExecuteAsync(Config(yaml), Options(repo.Path), CancellationToken.None);

        Assert.Contains(log.Events, e => e.Kind == RunEventKind.Info && e.Text.Contains("http://localhost:5173"));
    }
}
```

- [ ] **Step 2: Run and watch them fail**

Run: `dotnet test tests/QuickRun.Core.Tests --filter RunnerTests`
Expected: FAIL — `Runner` does not exist.

- [ ] **Step 3: Implement `Runner`**

Skeleton, with the parts that matter spelled out:

```csharp
using System.Collections.Concurrent;
using System.Text;
using QuickRun.Core.Config;
using QuickRun.Core.Process;
using QuickRun.Core.Requires;

namespace QuickRun.Core.Run;

public enum RunEventKind { Info, Output, Error, TaskStarted, TaskReady, TaskExited, Failed, Finished }

public sealed record RunEvent(RunEventKind Kind, string? Task, string Text);
public sealed record RunOutcome(bool Ok, string? Error);
public sealed record RunOptions(string Workspace, InterpolationContext Context,
    IReadOnlyDictionary<string, string> ExtraEnv, IReadOnlyList<string> Secrets,
    TimeSpan ReadyTimeout, bool SkipRequires = false);

public sealed class Runner(Action<RunEvent> onEvent) : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<string, StringBuilder> _logs = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _ready = new();
    private RunConfig? _config;
    private RunOptions? _options;

    public async Task<RunOutcome> ExecuteAsync(RunConfig config, RunOptions options, CancellationToken ct)
    {
        _config = config;
        _options = options;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _stop.Token);

        if (!options.SkipRequires && Blockers(config) is { Length: > 0 } blockers)
            return Fail(string.Join("\n", blockers));

        foreach (var step in Applicable(config.Setup))
        {
            var code = await RunOnceAsync(step, "setup", options, linked.Token);
            if (code != 0 && !step.ContinueOnError)
                return Fail($"setup step failed with exit code {code}: {Redact(step.Run, options)}");
        }

        foreach (var t in config.Tasks) _ready[t.Name] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = config.Tasks.Select(t => RunTaskAsync(t, options, linked.Token)).ToArray();
        await Task.WhenAll(running);

        if (linked.IsCancellationRequested) return new(false, "run cancelled");
        Emit(RunEventKind.Finished, null, "all tasks finished");
        return new(true, null);
    }

    public async Task StopAsync()
    {
        await _stop.CancelAsync();
        if (_config is null || _options is null) return;
        foreach (var step in Applicable(_config.Stop))
            await RunOnceAsync(step, "stop", _options, CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_stop.IsCancellationRequested) await _stop.CancelAsync();
        _stop.Dispose();
    }
}
```

Members to fill in:

| Member | Behaviour |
|---|---|
| `Blockers(config)` | `ToolChecker.CheckAll(config.Requires).Where(r => r.Blocks)` mapped to `"<describe> - install from <install>"` (the hint omitted when null); returns `string[]` |
| `Applicable(steps)` | filters out steps whose `When` is non-empty and excludes `OSKinds.Current.Key()` |
| `RunOnceAsync(step, phase, options, ct)` | expands `Run` and `Cwd` through `Interpolator`, builds a `ProcessSpec` with `ResolveCwd`, awaits `CommandRunner.StreamAsync`, forwarding each line as `Output`/`Error`, returns the exit code |
| `RunTaskAsync(task, options, ct)` | awaits each `DependsOn` entry's `_ready[name].Task` (with `options.ReadyTimeout`, treating a timeout as ready-enough and emitting an `Error` event saying so), emits `TaskStarted`, starts the process, starts a readiness watcher, awaits the exit, emits `TaskExited`, and applies `RestartPolicy.OnFailure` for at most three attempts with 1s/2s/4s backoff |
| readiness watcher | `Readiness.WaitAsync(task.ReadyWhen, () => _logs[task.Name].ToString(), options.ReadyTimeout, ct)`; on success completes `_ready[task.Name]`, emits `TaskReady`, and when `OpenUrl` is set — or `OpenReady` is true and `ReadyWhen.Http` or `ReadyWhen.Port` gives a URL — emits `Info` with `"open <url>"` |
| `ResolveCwd(workspace, cwd)` | `Path.GetFullPath(Path.Combine(workspace, cwd ?? "."))`, then verify the result is still under `workspace` and throw `InvalidOperationException` if not — defence in depth behind `ConfigValidator` |
| `Redact(text, options)` | `Interpolator.Redact(text, options.Secrets)` |
| `Emit` | wraps `onEvent`, redacting `Text` first; every event in the class goes through it |
| `Fail(message)` | emits a `Failed` event and returns `new RunOutcome(false, message)` |
| env for children | `options.ExtraEnv` merged over `config.Env` (task-level `Env` wins over both), all values interpolated |

A task with no `ReadyWhen` completes its `_ready` entry as soon as the process starts, so dependants
are not blocked forever.

`// ponytail: fixed 1s/2s/4s restart backoff, three attempts; make it configurable when someone asks`

- [ ] **Step 4: Run and watch them pass**

Run: `dotnet test tests/QuickRun.Core.Tests --filter RunnerTests`
Expected: PASS, 13 tests.

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test`
Expected: PASS. Core is complete at this point — every behaviour the CLI needs exists and is covered.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: process supervisor with dependencies, readiness, restart and stop steps"
```

---

### Task 14: CLI shell with `validate` and `detect`

**Files:**
- Create: `src/QuickRun.App/QuickRun.App.csproj`
- Create: `src/QuickRun.App/Program.cs`
- Create: `src/QuickRun.App/Commands/ValidateCommand.cs`
- Create: `src/QuickRun.App/Commands/DetectCommand.cs`
- Create: `src/QuickRun.App/Output.cs`
- Modify: `QuickRun.sln`
- Test: `tests/QuickRun.App.Tests/QuickRun.App.Tests.csproj`, `tests/QuickRun.App.Tests/ValidateCommandTests.cs`

**Interfaces:**
- Consumes: `ConfigParser`, `ConfigValidator` (Tasks 3, 4), `Detector` (Task 10), `OSKinds` (Task 2).
- Produces:
  - `static class Output` with `void Plan(RunPlan)`, `void Issues(IReadOnlyList<ValidationIssue>)`, `void Candidates(IReadOnlyList<Candidate>)`, `void Workspaces(IReadOnlyList<WorkspaceInfo>)`, `void Error(string)`, `void Info(string)` — every write to the console goes through this file so the commands stay testable and the formatting stays consistent.
  - `ValidateCommand` with `[CommandArgument(0, "[path]")] string? Path` and `[CommandOption("--json")] bool Json`.
  - `DetectCommand` with `[CommandArgument(0, "[path]")] string? Path`, `[CommandOption("--save")] bool Save`.
  - Exit codes: `0` success, `1` validation or detection found a problem, `2` bad usage or unreadable input.

`validate` and `detect` both operate on a **local path** (defaulting to the current directory), not a
repository URL. Running a remote repository is `run`'s job; keeping these local makes them usable as a
pre-commit check by repo owners, which is their main purpose.

- [ ] **Step 1: Create the CLI project**

```bash
cd C:/dev/privat/github/QuickRun
dotnet new console -o src/QuickRun.App -n QuickRun.App
dotnet new xunit -o tests/QuickRun.App.Tests -n QuickRun.App.Tests
rm tests/QuickRun.App.Tests/UnitTest1.cs
dotnet sln add src/QuickRun.App tests/QuickRun.App.Tests
dotnet add src/QuickRun.App reference src/QuickRun.Core
dotnet add tests/QuickRun.App.Tests reference src/QuickRun.App
dotnet add src/QuickRun.App package Spectre.Console.Cli
```

Then set the binary name in `src/QuickRun.App/QuickRun.App.csproj`:

```xml
<PropertyGroup>
  <AssemblyName>quickrun</AssemblyName>
  <RootNamespace>QuickRun.App</RootNamespace>
  <PublishSingleFile>true</PublishSingleFile>
  <SelfContained>true</SelfContained>
  <InvariantGlobalization>true</InvariantGlobalization>
</PropertyGroup>
```

- [ ] **Step 2: Write the failing tests**

`tests/QuickRun.App.Tests/ValidateCommandTests.cs`:

```csharp
using QuickRun.App.Commands;

namespace QuickRun.App.Tests;

public class ValidateCommandTests
{
    private static string WriteConfig(string yaml)
    {
        var dir = Path.Combine(Path.GetTempPath(), "quickrun-cli-" + Guid.NewGuid().ToString("n")[..8]);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "quickrun.yml"), yaml);
        return dir;
    }

    [Fact]
    public void A_valid_config_in_a_directory_returns_zero()
    {
        var dir = WriteConfig("run: ./run.sh");
        Assert.Equal(0, ValidateCommand.Check(dir, quiet: true).ExitCode);
    }

    [Fact]
    public void A_config_with_an_error_returns_one()
    {
        var dir = WriteConfig("tasks:\n  - name: a\n    run: x\n    dependsOn: [nope]");
        var result = ValidateCommand.Check(dir, quiet: true);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains(result.Issues, i => i.IsError);
    }

    [Fact]
    public void Malformed_yaml_returns_one_with_a_readable_message()
    {
        var dir = WriteConfig("run: [unclosed");
        var result = ValidateCommand.Check(dir, quiet: true);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("YAML", Assert.Single(result.Issues).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_directory_without_a_config_returns_two()
    {
        var dir = Path.Combine(Path.GetTempPath(), "quickrun-empty-" + Guid.NewGuid().ToString("n")[..8]);
        Directory.CreateDirectory(dir);
        Assert.Equal(2, ValidateCommand.Check(dir, quiet: true).ExitCode);
    }

    [Fact]
    public void An_explicit_file_path_is_accepted()
    {
        var dir = WriteConfig("run: ./run.sh");
        Assert.Equal(0, ValidateCommand.Check(Path.Combine(dir, "quickrun.yml"), quiet: true).ExitCode);
    }

    [Fact]
    public void Warnings_alone_still_return_zero()
    {
        var dir = WriteConfig("inputs:\n  - id: k\n    type: bool\n    pattern: \"^x\"\nrun: a");
        var result = ValidateCommand.Check(dir, quiet: true);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains(result.Issues, i => !i.IsError);
    }
}
```

The command's logic lives in a static `Check` method returning a record, with the Spectre command class
as a thin wrapper. That is what makes it testable without capturing console output.

- [ ] **Step 3: Run and watch them fail**

Run: `dotnet test tests/QuickRun.App.Tests`
Expected: FAIL — `ValidateCommand` does not exist.

- [ ] **Step 4: Implement `Output`**

`src/QuickRun.App/Output.cs` — every console write in the application:

```csharp
using QuickRun.Core.Config;
using QuickRun.Core.Detect;
using QuickRun.Core.Run;
using QuickRun.Core.Workspace;
using Spectre.Console;

namespace QuickRun.App;

public static class Output
{
    public static void Info(string text) => AnsiConsole.MarkupLineInterpolated($"[grey]{text}[/]");
    public static void Error(string text) => AnsiConsole.MarkupLineInterpolated($"[red]{text}[/]");
    public static void Warn(string text) => AnsiConsole.MarkupLineInterpolated($"[yellow]{text}[/]");

    public static void Issues(IReadOnlyList<ValidationIssue> issues)
    {
        foreach (var i in issues)
        {
            var prefix = i.IsError ? "[red]error[/]" : "[yellow]warning[/]";
            var where = string.IsNullOrEmpty(i.Path) ? "" : $" [grey]({i.Path})[/]";
            AnsiConsole.MarkupLineInterpolated($"{prefix}{where} {i.Message}");
        }
    }

    public static void Plan(RunPlan plan)
    {
        AnsiConsole.Write(new Rule($"[bold]{plan.DisplayName}[/]").LeftJustified());
        AnsiConsole.WriteLine(plan.Describe());
    }

    public static void Candidates(IReadOnlyList<Candidate> candidates)
    {
        var table = new Table().AddColumns("#", "kind", "directory", "commands");
        for (var i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            table.AddRow((i + 1).ToString(), c.Kind,
                c.RelativeDir.Length == 0 ? "." : c.RelativeDir,
                string.Join("\n", c.Setup.Concat(c.Run)));
        }
        AnsiConsole.Write(table);
    }

    public static void Workspaces(IReadOnlyList<WorkspaceInfo> workspaces)
    {
        var table = new Table().AddColumns("id", "ref", "size", "last used", "last run");
        foreach (var w in workspaces)
            table.AddRow(w.Id, w.Ref, Size(w.Bytes), w.LastUsed.LocalDateTime.ToString("yyyy-MM-dd HH:mm"),
                w.LastOk switch { true => "ok", false => "failed", null => "-" });
        AnsiConsole.Write(table);
    }

    private static string Size(long bytes) => bytes switch
    {
        > 1_073_741_824 => $"{bytes / 1_073_741_824.0:0.0} GB",
        > 1_048_576 => $"{bytes / 1_048_576.0:0.0} MB",
        > 1024 => $"{bytes / 1024.0:0.0} KB",
        _ => $"{bytes} B",
    };
}
```

`MarkupLineInterpolated` rather than `MarkupLine` throughout: repository names and log lines can
contain `[` and would otherwise be parsed as markup or throw.

- [ ] **Step 5: Implement `ValidateCommand`**

```csharp
using QuickRun.Core;
using QuickRun.Core.Config;
using Spectre.Console.Cli;

namespace QuickRun.App.Commands;

public sealed record ValidateResult(int ExitCode, IReadOnlyList<ValidationIssue> Issues, RunConfig? Config);

public sealed class ValidateCommand : Command<ValidateCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[path]")]
        [Description("Directory containing quickrun.yml, or the file itself. Defaults to the current directory.")]
        public string? Path { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var result = Check(settings.Path ?? Environment.CurrentDirectory, quiet: false);
        return result.ExitCode;
    }

    public static ValidateResult Check(string path, bool quiet)
    {
        var file = File.Exists(path) ? path : ConfigParser.FindConfigFile(path);
        if (file is null)
        {
            var issue = new ValidationIssue("", $"no quickrun.yml found in {path}", true);
            if (!quiet) Output.Error(issue.Message);
            return new(2, new[] { issue }, null);
        }

        RunConfig config;
        try
        {
            config = ConfigParser.Parse(File.ReadAllText(file), OSKinds.Current);
        }
        catch (ConfigException e)
        {
            var issue = new ValidationIssue(file, e.Message, true);
            if (!quiet) Output.Issues(new[] { issue });
            return new(1, new[] { issue }, null);
        }

        var issues = ConfigValidator.Validate(config);
        if (!quiet)
        {
            if (issues.Count == 0) Output.Info($"{file} is valid");
            else Output.Issues(issues);
        }
        return new(issues.Any(i => i.IsError) ? 1 : 0, issues, config);
    }
}
```

- [ ] **Step 6: Implement `DetectCommand`**

Same shape: a static `Find(string path)` returning `IReadOnlyList<Candidate>`, and `Execute` printing
them with `Output.Candidates`. With `--save`, write `Detector.ToYaml(candidates[0], null)` to
`quickrun.yml` in the scanned directory, refusing with exit code 1 if the file already exists — never
overwrite a config the owner wrote by hand. Exit code is 1 when nothing was detected.

- [ ] **Step 7: Wire up `Program.cs`**

```csharp
using QuickRun.App.Commands;
using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("quickrun");
    config.AddCommand<ValidateCommand>("validate")
          .WithDescription("Validate a quickrun.yml without running anything.")
          .WithExample("validate")
          .WithExample("validate", "./my-repo");
    config.AddCommand<DetectCommand>("detect")
          .WithDescription("Show how QuickRun would start a repository that has no config.")
          .WithExample("detect", ".", "--save");
    config.PropagateExceptions();
});

try
{
    return app.Run(args);
}
catch (Exception e)
{
    QuickRun.App.Output.Error(e.Message);
    return 2;
}
```

`PropagateExceptions` plus one handler means a stack trace never reaches a user, while the exception
message still does.

- [ ] **Step 8: Run the tests and watch them pass**

Run: `dotnet test tests/QuickRun.App.Tests`
Expected: PASS, 6 tests.

- [ ] **Step 9: Try it by hand**

```bash
dotnet run --project src/QuickRun.App -- validate --help
printf 'run: ./run.sh\n' > /tmp/qr-demo/quickrun.yml
dotnet run --project src/QuickRun.App -- validate /tmp/qr-demo
dotnet run --project src/QuickRun.App -- detect .
```

Expected: the help text lists both commands, `validate` reports the demo config as valid, and `detect`
on the QuickRun repository itself lists a `dotnet` candidate.

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "feat: quickrun CLI with validate and detect commands"
```

---

### Task 15: `quickrun run`

**Files:**
- Create: `src/QuickRun.App/Commands/RunCommand.cs`
- Create: `src/QuickRun.App/Prompts.cs`
- Modify: `src/QuickRun.App/Program.cs`
- Test: `tests/QuickRun.App.Tests/RunCommandTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 3–13.
- Produces:

```csharp
public sealed record RunArgs(string Repo, string? Ref, int? PullRequest, string? Subdir,
    IReadOnlyList<string> Inputs, string? Token, bool Fresh, bool Yes, bool NoOpen, string? ConfigPath);

public sealed record RunPreparation(int ExitCode, RunPlan? Plan, RunConfig? Config,
    string? Workspace, IReadOnlyDictionary<string, string?>? Values, string? Error);

public static class RunPipeline
{
    public static RunPreparation Prepare(RunArgs args, WorkspaceStore store, GitClient git,
        Func<IReadOnlyList<InputDef>, IReadOnlyDictionary<string, string?>, IReadOnlyDictionary<string, string?>> collectInputs);
}
```

`Prepare` does everything up to but not including execution: normalise the URL, check out, load the
config or detect a candidate, collect and validate inputs, and build the `RunPlan`. It never prints and
never executes, so it can be tested end to end against a `LocalRepo`. `RunCommand.Execute` then prints
the plan, asks for confirmation, and hands the config to `Runner`.

The `collectInputs` delegate is how the console prompt is injected — in tests it returns a fixed
dictionary, in the CLI it is `Prompts.Collect`.

- [ ] **Step 1: Write the failing tests**

`tests/QuickRun.App.Tests/RunCommandTests.cs`:

```csharp
using QuickRun.App.Commands;
using QuickRun.Core.Config;
using QuickRun.Core.Git;
using QuickRun.Core.Process;
using QuickRun.Core.Workspace;

namespace QuickRun.App.Tests;

public class RunCommandTests
{
    private static GitClient Git() => new(new CredentialResolver(null, (_, _) => new CommandResult(1, "", false)));

    private static RunArgs Args(string repo, string? @ref = "main", params string[] inputs) =>
        new(repo, @ref, null, null, inputs, null, false, true, false, null);

    private static RunPreparation Prepare(RunArgs args, TempHome home,
        IReadOnlyDictionary<string, string?>? answers = null) =>
        RunPipeline.Prepare(args, new WorkspaceStore(home.Path), Git(),
            (_, provided) => answers ?? provided);

    [Fact]
    public void A_repository_with_a_config_produces_a_plan()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("quickrun.yml", "name: Demo\nrun: echo hi\n");
        repo.Commit("add config");

        var prep = Prepare(Args(repo.Url), home);

        Assert.Equal(0, prep.ExitCode);
        Assert.Equal("Demo", prep.Plan!.DisplayName);
        Assert.Equal("echo hi", Assert.Single(prep.Plan.Commands).Command);
        Assert.Equal(repo.Head(), prep.Plan.Commit);
    }

    [Fact]
    public void The_workspace_is_created_under_the_store_root()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("quickrun.yml", "run: echo hi\n");
        repo.Commit("add config");

        var prep = Prepare(Args(repo.Url), home);

        Assert.StartsWith(home.Path, prep.Workspace!);
        Assert.True(File.Exists(Path.Combine(prep.Workspace!, "quickrun.yml")));
    }

    [Fact]
    public void A_repository_without_a_config_falls_back_to_detection()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("package.json", "{\"scripts\":{\"dev\":\"vite\"}}");
        repo.Commit("add package.json");

        var prep = Prepare(Args(repo.Url), home);

        Assert.Equal(0, prep.ExitCode);
        Assert.Contains(prep.Plan!.Commands, c => c.Command.Contains("npm run dev"));
    }

    [Fact]
    public void A_repository_with_neither_config_nor_detectable_entry_point_fails()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();

        var prep = Prepare(Args(repo.Url), home);

        Assert.Equal(1, prep.ExitCode);
        Assert.Contains("quickrun.yml", prep.Error!);
    }

    [Fact]
    public void A_root_run_script_is_used_when_there_is_no_config()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("run.sh", "#!/bin/sh\necho from-script\n");
        repo.Commit("add script");

        var prep = Prepare(Args(repo.Url), home);

        Assert.Equal(0, prep.ExitCode);
        Assert.Contains("run.sh", Assert.Single(prep.Plan!.Commands).Command);
    }

    [Fact]
    public void An_invalid_config_fails_before_any_plan_is_built()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("quickrun.yml", "tasks:\n  - name: a\n    run: x\n    dependsOn: [nope]\n");
        repo.Commit("add bad config");

        var prep = Prepare(Args(repo.Url), home);

        Assert.Equal(1, prep.ExitCode);
        Assert.Null(prep.Plan);
    }

    [Fact]
    public void Input_assignments_are_interpolated_into_the_plan()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("quickrun.yml", "inputs:\n  - id: apiKey\n    required: true\nrun: ./app --key ${inputs.apiKey}\n");
        repo.Commit("add config");

        var prep = Prepare(Args(repo.Url, "main", "apiKey=sk-1"), home);

        Assert.Equal(0, prep.ExitCode);
        Assert.Equal("./app --key sk-1", Assert.Single(prep.Plan!.Commands).Command);
    }

    [Fact]
    public void A_missing_required_input_fails_when_the_collector_supplies_nothing()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("quickrun.yml", "inputs:\n  - id: apiKey\n    required: true\nrun: ./app\n");
        repo.Commit("add config");

        var prep = Prepare(Args(repo.Url), home);

        Assert.Equal(1, prep.ExitCode);
        Assert.Contains("apiKey", prep.Error!);
    }

    [Fact]
    public void The_collector_can_supply_a_missing_required_input()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("quickrun.yml", "inputs:\n  - id: apiKey\n    required: true\nrun: ./app --key ${inputs.apiKey}\n");
        repo.Commit("add config");

        var prep = Prepare(Args(repo.Url), home,
            answers: new Dictionary<string, string?> { ["apiKey"] = "sk-prompted" });

        Assert.Equal(0, prep.ExitCode);
        Assert.Contains("sk-prompted", prep.Plan!.Commands[0].Command);
    }

    [Fact]
    public void An_unknown_ref_fails_with_the_git_error()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        var prep = Prepare(Args(repo.Url, "no-such-branch"), home);
        Assert.Equal(1, prep.ExitCode);
        Assert.NotNull(prep.Error);
    }

    [Fact]
    public void A_subdir_scopes_the_config_lookup()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("web/quickrun.yml", "name: Web\nrun: npm run dev\n");
        repo.Commit("add nested config");

        var prep = RunPipeline.Prepare(
            new RunArgs(repo.Url, "main", null, "web", Array.Empty<string>(), null, false, true, false, null),
            new WorkspaceStore(home.Path), Git(), (_, provided) => provided);

        Assert.Equal(0, prep.ExitCode);
        Assert.Equal("Web", prep.Plan!.DisplayName);
    }

    [Fact]
    public void A_malformed_input_assignment_returns_usage_failure()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("quickrun.yml", "run: ./app\n");
        repo.Commit("add config");

        var prep = Prepare(Args(repo.Url, "main", "no-equals-sign"), home);

        Assert.Equal(2, prep.ExitCode);
    }

    [Fact]
    public void The_workspace_metadata_records_the_commit()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("quickrun.yml", "run: echo hi\n");
        repo.Commit("add config");

        Prepare(Args(repo.Url), home);

        var info = Assert.Single(new WorkspaceStore(home.Path).List());
        Assert.Equal(repo.Head(), info.LastCommit);
    }
}
```

`LocalRepo` and `TempHome` live in the Core test project. Add a shared link rather than copying them:

```xml
<ItemGroup>
  <Compile Include="../QuickRun.Core.Tests/LocalRepo.cs" Link="Helpers/LocalRepo.cs" />
  <Compile Include="../QuickRun.Core.Tests/TempHome.cs" Link="Helpers/TempHome.cs" />
</ItemGroup>
```

- [ ] **Step 2: Run and watch them fail**

Run: `dotnet test tests/QuickRun.App.Tests --filter RunCommandTests`
Expected: FAIL — `RunPipeline` does not exist.

- [ ] **Step 3: Implement `RunPipeline.Prepare`**

Order of operations, each failure returning immediately:

1. `GitClient.NormalizeRepoUrl(args.Repo)` — but only when the input is not already an absolute URI
   with a scheme git understands. Catch `ArgumentException` and return exit code 2. (The tests pass
   `file://` URLs, so treat any absolute `Uri` whose scheme is `file`, `http`, `https` or `ssh` as
   already normalised.)
2. `InputResolver.ParseAssignments(args.Inputs)` — `ArgumentException` returns exit code 2.
3. `store.PathFor(repo, ref)` and `git.CheckoutOrUpdate(repo, ref, args.PullRequest, path, args.Fresh)`
   — a failed outcome returns exit code 1 with the (already scrubbed) error.
4. `store.Touch(id, repo, ref, outcome.Commit, null)`.
5. Resolve the config root: `Path.Combine(workspace, args.Subdir ?? "")`, verified to stay under the
   workspace.
6. Load the config, in order: `args.ConfigPath` if given, else `ConfigParser.FindConfigFile(root)`,
   else `ConfigParser.FindRootScript(root, OSKinds.Current)` wrapped as a one-task config, else
   `Detector.Detect(root, OSKinds.Current)`. With no candidates, exit code 1 and the message
   `"no quickrun.yml, no run script and nothing detectable in <repo> - see https://fgilde.github.io/QuickRun/docs/config"`.
   With several candidates, take the highest-confidence one and record the rest so the caller can
   mention them.
7. `ConfigValidator.Validate(config)` — any error returns exit code 1 after the issues are attached to
   the preparation record.
8. `InputResolver.ApplyDefaults` then `InputResolver.Validate`; if there are errors, call
   `collectInputs(config.Inputs, values)` once and validate again. Still failing means exit code 1 with
   the joined error messages.
9. `RunPlanBuilder.Build(config, ctx, OSKinds.Current, repo, ref, commit)` where `ctx` is an
   `InterpolationContext(values, workspace, repoName, ref)`.

- [ ] **Step 4: Implement `Prompts.Collect`**

```csharp
using QuickRun.Core.Config;
using Spectre.Console;

namespace QuickRun.App;

public static class Prompts
{
    public static IReadOnlyDictionary<string, string?> Collect(
        IReadOnlyList<InputDef> defs, IReadOnlyDictionary<string, string?> provided)
    {
        var values = new Dictionary<string, string?>(provided);
        foreach (var d in defs)
        {
            if (values.TryGetValue(d.Id, out var existing) && !string.IsNullOrWhiteSpace(existing)) continue;
            if (!d.Required && string.IsNullOrWhiteSpace(d.Default) && !AnsiConsole.Profile.Capabilities.Interactive) continue;

            values[d.Id] = d.Type switch
            {
                InputType.Bool => AnsiConsole.Confirm(Label(d), bool.TryParse(d.Default, out var b) && b).ToString(),
                InputType.Select => AnsiConsole.Prompt(
                    new SelectionPrompt<string>().Title(Label(d)).AddChoices(d.Options.Select(o => o.Value))),
                InputType.Password => Ask(d, secret: true),
                _ => Ask(d, secret: false),
            };
        }
        return values;
    }

    private static string Label(InputDef d) =>
        (d.Label ?? d.Id) + (string.IsNullOrWhiteSpace(d.Description) ? "" : $" [grey]({d.Description})[/]");

    private static string Ask(InputDef d, bool secret)
    {
        var prompt = new TextPrompt<string>(Label(d)).AllowEmpty();
        if (secret) prompt.Secret();
        if (!string.IsNullOrWhiteSpace(d.Default)) prompt.DefaultValue(d.Default!);
        return AnsiConsole.Prompt(prompt);
    }
}
```

A non-interactive console (piped output, CI) must not hang: when `--yes` is set, `RunCommand` passes a
collector that returns `provided` unchanged instead of `Prompts.Collect`, so a missing required input
fails with a message rather than blocking on a prompt nobody can answer.

- [ ] **Step 5: Implement `RunCommand.Execute`**

```csharp
public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
{
    var store = new WorkspaceStore();
    var git = new GitClient(new CredentialResolver(settings.Token));
    var args = settings.ToArgs();

    var prep = RunPipeline.Prepare(args, store, git,
        settings.Yes ? (_, provided) => provided : Prompts.Collect);

    if (prep.ExitCode != 0)
    {
        if (prep.Error is { } error) Output.Error(error);
        return prep.ExitCode;
    }

    Output.Plan(prep.Plan!);
    if (!settings.Yes && !AnsiConsole.Confirm("Run these commands?", defaultValue: false))
    {
        Output.Info("cancelled");
        return 0;
    }

    var secrets = Interpolator.Secrets(prep.Values!, InputResolver.SecretIds(prep.Config!.Inputs));
    var options = new RunOptions(prep.Workspace!, Context(prep), InputResolver.ToEnv(prep.Config.Inputs, prep.Values!),
        secrets, Readiness.DefaultTimeout);

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    await using var runner = new Runner(e => Report(e, settings.NoOpen));
    var outcome = await runner.ExecuteAsync(prep.Config, options, cts.Token);
    await runner.StopAsync();

    store.Touch(WorkspaceStore.IdFor(args.Repo, args.Ref ?? "main"), args.Repo, args.Ref ?? "main",
        prep.Plan!.Commit, outcome.Ok);

    if (!outcome.Ok) Output.Error(outcome.Error ?? "run failed");
    return outcome.Ok ? 0 : 1;
}
```

`Report` writes `Output` lines per event kind and, for an `Info` event whose text starts with `open `,
launches the URL with `Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })` unless
`--no-open` was passed. That is the one place in the CLI that opens a browser; Core never does.

Settings for the command:

```csharp
[CommandArgument(0, "<repo>")]         public string Repo { get; init; } = "";
[CommandOption("-r|--ref")]            public string? Ref { get; init; }
[CommandOption("-p|--pr")]             public int? PullRequest { get; init; }
[CommandOption("-d|--subdir")]         public string? Subdir { get; init; }
[CommandOption("-i|--input")]          public string[] Inputs { get; init; } = [];
[CommandOption("-t|--token")]          public string? Token { get; init; }
[CommandOption("-c|--config")]         public string? ConfigPath { get; init; }
[CommandOption("--fresh")]             public bool Fresh { get; init; }
[CommandOption("-y|--yes")]            public bool Yes { get; init; }
[CommandOption("--no-open")]           public bool NoOpen { get; init; }
```

`--ref` defaults to the repository's default branch. Resolve that by taking the first entry of
`git.ListBranches` that equals `main`, then `master`, then whatever comes first — and if branch listing
fails, fall back to `HEAD`, which `git clone --branch` accepts.

Register it in `Program.cs`:

```csharp
config.AddCommand<RunCommand>("run")
      .WithDescription("Check out a repository and run it.")
      .WithExample("run", "acme/app")
      .WithExample("run", "acme/app", "--ref", "feature/login")
      .WithExample("run", "https://github.com/acme/app", "--pr", "42", "--input", "apiKey=sk-1");
```

- [ ] **Step 6: Run the tests and watch them pass**

Run: `dotnet test tests/QuickRun.App.Tests`
Expected: PASS, 19 tests.

- [ ] **Step 7: Run it against a real repository**

```bash
dotnet run --project src/QuickRun.App -- run fgilde/QuickRun --ref main --yes --no-open
```

Expected: the repository is cloned into the workspace root, the plan is printed, and QuickRun's own
`quickrun.yml` (added in Task 17) executes. Before Task 17 exists, expect the detection fallback to
offer a `dotnet` candidate instead — that is the correct behaviour at this point.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: quickrun run with checkout, detection fallback, prompts and confirmation"
```

---

### Task 16: `quickrun ls` and `quickrun clean`

**Files:**
- Create: `src/QuickRun.App/Commands/WorkspaceCommands.cs`
- Modify: `src/QuickRun.App/Program.cs`
- Test: `tests/QuickRun.App.Tests/WorkspaceCommandsTests.cs`

**Interfaces:**
- Consumes: `WorkspaceStore`, `WorkspaceInfo` (Task 8), `Output` (Task 14).
- Produces:

```csharp
public sealed record CleanRequest(bool All, TimeSpan? OlderThan, string? Id);
public sealed record CleanResult(int ExitCode, int Removed, string? Error);

public static class WorkspaceOps
{
    public static TimeSpan? ParseAge(string? text);           // "30d", "12h", "2w" -> TimeSpan; null for null
    public static CleanResult Clean(WorkspaceStore store, CleanRequest request);
}
```

`ls` needs no logic beyond `store.List()` and `Output.Workspaces`, so it has no separate ops type. The
clean rules do have logic worth pinning down: exactly one of `--all`, `--older-than` or a workspace id
must be given, and `clean` with none of them is a usage error rather than a silent no-op — deleting
everything by default would be the worst possible guess.

- [ ] **Step 1: Write the failing tests**

`tests/QuickRun.App.Tests/WorkspaceCommandsTests.cs`:

```csharp
using QuickRun.App.Commands;
using QuickRun.Core.Workspace;

namespace QuickRun.App.Tests;

public class WorkspaceCommandsTests
{
    private static WorkspaceStore Seed(TempHome home, params string[] names)
    {
        var store = new WorkspaceStore(home.Path);
        foreach (var n in names)
        {
            var url = $"https://github.com/acme/{n}";
            Directory.CreateDirectory(store.PathFor(url, "main"));
            store.Touch(WorkspaceStore.IdFor(url, "main"), url, "main", null, null);
        }
        return store;
    }

    [Theory]
    [InlineData("30d", 30 * 24)]
    [InlineData("12h", 12)]
    [InlineData("2w", 14 * 24)]
    public void ParseAge_understands_days_hours_and_weeks(string text, double expectedHours)
        => Assert.Equal(expectedHours, WorkspaceOps.ParseAge(text)!.Value.TotalHours, 3);

    [Fact]
    public void ParseAge_returns_null_for_null()
        => Assert.Null(WorkspaceOps.ParseAge(null));

    [Theory]
    [InlineData("30")]
    [InlineData("30x")]
    [InlineData("d30")]
    public void ParseAge_rejects_anything_else(string text)
        => Assert.Throws<ArgumentException>(() => WorkspaceOps.ParseAge(text));

    [Fact]
    public void Clean_without_any_selector_is_a_usage_error()
    {
        using var home = new TempHome();
        var result = WorkspaceOps.Clean(Seed(home, "a"), new CleanRequest(false, null, null));
        Assert.Equal(2, result.ExitCode);
        Assert.Equal(0, result.Removed);
    }

    [Fact]
    public void Clean_with_more_than_one_selector_is_a_usage_error()
    {
        using var home = new TempHome();
        var result = WorkspaceOps.Clean(Seed(home, "a"),
            new CleanRequest(true, TimeSpan.FromDays(1), null));
        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public void Clean_all_removes_every_workspace()
    {
        using var home = new TempHome();
        var store = Seed(home, "a", "b", "c");
        var result = WorkspaceOps.Clean(store, new CleanRequest(true, null, null));
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(3, result.Removed);
        Assert.Empty(store.List());
    }

    [Fact]
    public void Clean_by_id_removes_only_that_workspace()
    {
        using var home = new TempHome();
        var store = Seed(home, "a", "b");
        var id = store.List().First().Id;

        var result = WorkspaceOps.Clean(store, new CleanRequest(false, null, id));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, result.Removed);
        Assert.Single(store.List());
    }

    [Fact]
    public void Clean_by_an_unknown_id_reports_an_error()
    {
        using var home = new TempHome();
        var result = WorkspaceOps.Clean(Seed(home, "a"), new CleanRequest(false, null, "not-a-workspace"));
        Assert.Equal(1, result.ExitCode);
        Assert.Equal(0, result.Removed);
    }

    [Fact]
    public void Clean_by_age_keeps_recent_workspaces()
    {
        using var home = new TempHome();
        var store = Seed(home, "a", "b");
        var result = WorkspaceOps.Clean(store, new CleanRequest(false, TimeSpan.FromDays(30), null));
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, result.Removed);
        Assert.Equal(2, store.List().Count);
    }

    [Fact]
    public void Clean_rejects_an_id_that_tries_to_escape_the_root()
    {
        using var home = new TempHome();
        var result = WorkspaceOps.Clean(Seed(home, "a"), new CleanRequest(false, null, "../../windows"));
        Assert.Equal(1, result.ExitCode);
    }
}
```

- [ ] **Step 2: Run and watch them fail**

Run: `dotnet test tests/QuickRun.App.Tests --filter WorkspaceCommandsTests`
Expected: FAIL — `WorkspaceOps` does not exist.

- [ ] **Step 3: Implement `WorkspaceOps` and the two commands**

```csharp
using System.Text.RegularExpressions;
using QuickRun.Core.Workspace;
using Spectre.Console;
using Spectre.Console.Cli;

namespace QuickRun.App.Commands;

public sealed record CleanRequest(bool All, TimeSpan? OlderThan, string? Id);
public sealed record CleanResult(int ExitCode, int Removed, string? Error);

public static class WorkspaceOps
{
    public static TimeSpan? ParseAge(string? text)
    {
        if (text is null) return null;
        var m = Regex.Match(text.Trim(), @"^(\d+)([hdw])$", RegexOptions.IgnoreCase);
        if (!m.Success) throw new ArgumentException($"expected a duration like 30d, 12h or 2w, got '{text}'");
        var n = int.Parse(m.Groups[1].Value);
        return m.Groups[2].Value.ToLowerInvariant() switch
        {
            "h" => TimeSpan.FromHours(n),
            "w" => TimeSpan.FromDays(7 * n),
            _ => TimeSpan.FromDays(n),
        };
    }

    public static CleanResult Clean(WorkspaceStore store, CleanRequest request)
    {
        var selectors = new[] { request.All, request.OlderThan is not null, request.Id is not null }.Count(x => x);
        if (selectors != 1)
            return new(2, 0, "specify exactly one of --all, --older-than <age> or a workspace id");

        if (request.All) return new(0, store.RemoveAll(), null);
        if (request.OlderThan is { } age) return new(0, store.Clean(age), null);

        try
        {
            return store.Remove(request.Id!)
                ? new(0, 1, null)
                : new(1, 0, $"no workspace with id '{request.Id}'");
        }
        catch (ArgumentException e)
        {
            return new(1, 0, e.Message);
        }
    }
}

public sealed class ListCommand : Command<ListCommand.Settings>
{
    public sealed class Settings : CommandSettings { }

    public override int Execute(CommandContext context, Settings settings)
    {
        var workspaces = new WorkspaceStore().List();
        if (workspaces.Count == 0) { Output.Info("no workspaces yet"); return 0; }
        Output.Workspaces(workspaces);
        Output.Info($"{workspaces.Count} workspace(s), {workspaces.Sum(w => w.Bytes) / 1_048_576} MB total");
        return 0;
    }
}

public sealed class CleanCommand : Command<CleanCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[id]")] public string? Id { get; init; }
        [CommandOption("--all")] public bool All { get; init; }
        [CommandOption("--older-than")] public string? OlderThan { get; init; }
        [CommandOption("-y|--yes")] public bool Yes { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        TimeSpan? age;
        try { age = WorkspaceOps.ParseAge(settings.OlderThan); }
        catch (ArgumentException e) { Output.Error(e.Message); return 2; }

        if (settings.All && !settings.Yes && !AnsiConsole.Confirm("Delete every workspace?", false))
        {
            Output.Info("cancelled");
            return 0;
        }

        var result = WorkspaceOps.Clean(new WorkspaceStore(), new CleanRequest(settings.All, age, settings.Id));
        if (result.Error is { } error) Output.Error(error);
        else Output.Info($"removed {result.Removed} workspace(s)");
        return result.ExitCode;
    }
}
```

Register both in `Program.cs`:

```csharp
config.AddCommand<ListCommand>("ls")
      .WithDescription("List checked-out workspaces with their size and last use.");
config.AddCommand<CleanCommand>("clean")
      .WithDescription("Remove workspaces.")
      .WithExample("clean", "--all")
      .WithExample("clean", "--older-than", "30d")
      .WithExample("clean", "acme__app__main-1a2b3c");
```

- [ ] **Step 4: Run the tests and watch them pass**

Run: `dotnet test tests/QuickRun.App.Tests --filter WorkspaceCommandsTests`
Expected: PASS, 13 tests.

- [ ] **Step 5: Verify by hand**

```bash
dotnet run --project src/QuickRun.App -- ls
dotnet run --project src/QuickRun.App -- clean --older-than 30d
```

Expected: the table lists whatever Task 15's manual run left behind, and `clean --older-than 30d`
removes nothing because it is all fresh.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: workspace listing and cleanup commands"
```

---

### Task 17: Samples, schema validation in CI, README and release build

**Files:**
- Create: `samples/dotnet-web.yml`, `samples/npm-dev.yml`, `samples/python-venv.yml`, `samples/multi-service.yml`, `samples/docker-compose.yml`, `samples/inputs-and-secrets.yml`, `samples/platform-scripts.yml`, `samples/install-dotnet-then-run.yml`
- Create: `quickrun.yml` (QuickRun running itself)
- Create: `README.md`
- Create: `.github/workflows/ci.yml`
- Create: `.github/workflows/release.yml`
- Test: `tests/QuickRun.Core.Tests/SamplesTests.cs`

**Interfaces:**
- Consumes: `ConfigParser`, `ConfigValidator`.
- Produces: no new API. This task locks the documented examples to the engine.

- [ ] **Step 1: Write the failing sample test**

`tests/QuickRun.Core.Tests/SamplesTests.cs`:

```csharp
using QuickRun.Core;
using QuickRun.Core.Config;

namespace QuickRun.Core.Tests;

public class SamplesTests
{
    private static string SamplesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "samples"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("samples/ not found"), "samples");
    }

    public static TheoryData<string> SampleFiles()
    {
        var data = new TheoryData<string>();
        foreach (var f in Directory.GetFiles(SamplesDir(), "*.yml")) data.Add(Path.GetFileName(f));
        return data;
    }

    [Fact]
    public void There_is_at_least_one_sample()
        => Assert.NotEmpty(Directory.GetFiles(SamplesDir(), "*.yml"));

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void Every_sample_parses_and_validates_on_every_platform(string fileName)
    {
        var yaml = File.ReadAllText(Path.Combine(SamplesDir(), fileName));
        foreach (var os in new[] { OSKind.Windows, OSKind.Linux, OSKind.MacOs })
        {
            var config = ConfigParser.Parse(yaml, os);
            var errors = ConfigValidator.Validate(config).Where(i => i.IsError).ToList();
            Assert.Empty(errors);
        }
    }

    [Fact]
    public void Quickrun_own_config_parses_and_validates()
    {
        var root = Directory.GetParent(SamplesDir())!.FullName;
        var yaml = File.ReadAllText(Path.Combine(root, "quickrun.yml"));
        var errors = ConfigValidator.Validate(ConfigParser.Parse(yaml, OSKinds.Current))
            .Where(i => i.IsError).ToList();
        Assert.Empty(errors);
    }
}
```

The per-platform loop is the point: a sample using a bare `run: ./run.sh` would pass on Linux and fail
the platform check on Windows, and the test catches that before the docs ship it.

- [ ] **Step 2: Run and watch it fail**

Run: `dotnet test tests/QuickRun.Core.Tests --filter SamplesTests`
Expected: FAIL — `samples/ not found`.

- [ ] **Step 3: Write the samples**

Every sample carries the schema comment and a leading `# What this shows:` line. Eight files:

`samples/npm-dev.yml`

```yaml
# yaml-language-server: $schema=https://fgilde.github.io/QuickRun/quickrun.schema.json
# What this shows: the smallest useful config - check node, install, start a dev server.
version: 1
name: Vite dev server
requires:
  - tool: node
    version: ">=20"
    install: https://nodejs.org/en/download
setup:
  - npm ci
tasks:
  - name: web
    run: npm run dev
    readyWhen: {port: 5173}
    open: true
```

`samples/dotnet-web.yml`

```yaml
# yaml-language-server: $schema=https://fgilde.github.io/QuickRun/quickrun.schema.json
# What this shows: verifying an SDK version before running an ASP.NET Core app.
version: 1
name: ASP.NET Core API
requires:
  - tool: dotnet
    version: ">=9.0"
    install: https://dot.net
env:
  ASPNETCORE_ENVIRONMENT: Development
setup:
  - dotnet restore
tasks:
  - name: api
    run: dotnet run --project src/Api
    readyWhen: {log: "Now listening on: (?<url>\\S+)"}
    open: true
```

`samples/python-venv.yml`

```yaml
# yaml-language-server: $schema=https://fgilde.github.io/QuickRun/quickrun.schema.json
# What this shows: a virtual environment, and how platform maps handle the venv layout difference.
version: 1
name: Flask app
requires:
  - tool: python
    version: ">=3.11"
    install: https://www.python.org/downloads/
setup:
  - run:
      windows: python -m venv .venv
      linux: python3 -m venv .venv
      macos: python3 -m venv .venv
  - run:
      windows: .venv\Scripts\pip install -r requirements.txt
      linux: .venv/bin/pip install -r requirements.txt
      macos: .venv/bin/pip install -r requirements.txt
tasks:
  - name: web
    run:
      windows: .venv\Scripts\python app.py
      linux: .venv/bin/python app.py
      macos: .venv/bin/python app.py
    readyWhen: {port: 5000}
    open: true
```

`samples/multi-service.yml`

```yaml
# yaml-language-server: $schema=https://fgilde.github.io/QuickRun/quickrun.schema.json
# What this shows: three runtimes at once, started in dependency order, cleaned up on stop.
version: 1
name: Full stack
requires:
  - tool: docker
    install: https://docs.docker.com/get-docker/
  - tool: dotnet
    version: ">=9.0"
  - tool: node
    version: ">=20"
setup:
  - run: npm ci
    cwd: web
  - dotnet restore
tasks:
  - name: db
    run: docker compose up -d postgres
    readyWhen: {port: 5432}
  - name: api
    run: dotnet run --project src/Api
    dependsOn: [db]
    env:
      ConnectionStrings__Default: Host=localhost;Database=app;Username=postgres;Password=postgres
    readyWhen: {http: "http://localhost:5000/health"}
    restart: onFailure
  - name: web
    run: npm run dev
    cwd: web
    dependsOn: [api]
    readyWhen: {port: 5173}
    open: true
stop:
  - docker compose down
```

`samples/docker-compose.yml`

```yaml
# yaml-language-server: $schema=https://fgilde.github.io/QuickRun/quickrun.schema.json
# What this shows: wrapping an existing compose file, including teardown.
version: 1
name: Compose stack
requires:
  - tool: docker
    install: https://docs.docker.com/get-docker/
tasks:
  - name: stack
    run: docker compose up
    readyWhen: {http: "http://localhost:8080"}
    open: true
stop:
  - docker compose down -v
```

`samples/inputs-and-secrets.yml`

```yaml
# yaml-language-server: $schema=https://fgilde.github.io/QuickRun/quickrun.schema.json
# What this shows: a generated form - required secret, validated pattern, number range, dropdown.
version: 1
name: AI chat demo
inputs:
  - id: apiKey
    label: OpenAI API key
    type: password
    description: Created at platform.openai.com
    required: true
    pattern: "^sk-"
    env: OPENAI_API_KEY
  - id: port
    label: Port
    type: number
    default: "3000"
    min: 1024
    max: 65535
    env: PORT
  - id: model
    label: Model
    type: select
    options: [gpt-4o-mini, gpt-4o]
    default: gpt-4o-mini
    env: MODEL
  - id: verbose
    label: Verbose logging
    type: bool
    default: "false"
    env: VERBOSE
setup:
  - npm ci
tasks:
  - name: app
    run: npm start
    readyWhen: {port: 3000}
    open: "http://localhost:${inputs.port}"
```

`samples/platform-scripts.yml`

```yaml
# yaml-language-server: $schema=https://fgilde.github.io/QuickRun/quickrun.schema.json
# What this shows: one script per platform, the shortest possible config.
version: 1
name: Script runner
run:
  windows: powershell -ExecutionPolicy Bypass -File ./run.ps1
  linux: ./run.sh
  macos: ./run.sh
```

`samples/install-dotnet-then-run.yml`

```yaml
# yaml-language-server: $schema=https://fgilde.github.io/QuickRun/quickrun.schema.json
# What this shows: a repo that brings its own SDK, so there is nothing to require.
version: 1
name: Self-contained .NET sample
setup:
  - run:
      linux: curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh && chmod +x dotnet-install.sh && ./dotnet-install.sh -c 10.0 --install-dir ./dotnet10
      macos: curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh && chmod +x dotnet-install.sh && ./dotnet-install.sh -c 10.0 --install-dir ./dotnet10
      windows: powershell -NoProfile -Command "Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1; ./dotnet-install.ps1 -Channel 10.0 -InstallDir ./dotnet10"
  - run:
      linux: ./dotnet10/dotnet restore
      macos: ./dotnet10/dotnet restore
      windows: .\dotnet10\dotnet restore
tasks:
  - name: sample
    run:
      linux: ./dotnet10/dotnet run --project ./Samples/MainSample.WebAssembly/MainSample.WebAssembly.csproj
      macos: ./dotnet10/dotnet run --project ./Samples/MainSample.WebAssembly/MainSample.WebAssembly.csproj
      windows: .\dotnet10\dotnet run --project .\Samples\MainSample.WebAssembly\MainSample.WebAssembly.csproj
    readyWhen: {log: "Now listening on: (?<url>\\S+)"}
    open: true
```

- [ ] **Step 4: Write QuickRun's own `quickrun.yml`**

```yaml
# yaml-language-server: $schema=https://fgilde.github.io/QuickRun/quickrun.schema.json
# QuickRun runs itself: this is what the extension button does on this repository.
version: 1
name: QuickRun
description: Build and test QuickRun, then show the CLI help.
icon: assets/icon.png
docs: https://fgilde.github.io/QuickRun
requires:
  - tool: dotnet
    version: ">=10.0"
    install: https://dot.net
setup:
  - dotnet restore
  - dotnet test --nologo
tasks:
  - name: cli
    run: dotnet run --project src/QuickRun.App -- --help
```

- [ ] **Step 5: Run the sample tests and watch them pass**

Run: `dotnet test tests/QuickRun.Core.Tests --filter SamplesTests`
Expected: PASS — one test per sample file plus two.

If `inputs-and-secrets.yml` fails, the cause is `open: "http://localhost:${inputs.port}"`:
`ConfigValidator.ValidateInterpolation` must include `OpenUrl` in the strings it checks, and `port`
must therefore be a declared input. It is — this is the test that proves the validator looks there.

- [ ] **Step 6: Write the README**

`README.md`, English, structured as: one-sentence description, the logo from `assets/logo.png`, a
30-second example (`quickrun run acme/app`), what a `quickrun.yml` looks like (the `npm-dev.yml`
sample inline), the CLI reference table, install instructions per OS, a security section stating
plainly that a trusted repository's commands run with the user's privileges and that the confirmation
dialog is not skippable, links to both language landing pages
(`https://fgilde.github.io/QuickRun/` and `/de/`), and a link to `samples/`.

Do **not** document the browser extension, the daemon, the protocol handler or the store links yet —
they arrive in Phases 2 and 3, and a README promising them now would be false.

- [ ] **Step 7: Write the CI workflow**

`.github/workflows/ci.yml`:

```yaml
name: ci
on:
  push:
    branches: [main]
  pull_request:

jobs:
  test:
    strategy:
      fail-fast: false
      matrix:
        os: [ubuntu-latest, windows-latest, macos-latest]
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - run: dotnet restore
      - run: dotnet build --no-restore -warnaserror
      - run: dotnet test --no-build --verbosity normal
```

The three-OS matrix is not optional for this project: the shell resolution, the venv layout, the
process-tree kill and the workspace root all differ per platform, and every one of them has a test.

- [ ] **Step 8: Write the release workflow**

`.github/workflows/release.yml`, triggered on a `v*` tag, with a matrix over the six runtime
identifiers from the spec:

```yaml
name: release
on:
  push:
    tags: ['v*']

jobs:
  build:
    strategy:
      matrix:
        include:
          - { os: windows-latest, rid: win-x64 }
          - { os: windows-latest, rid: win-arm64 }
          - { os: ubuntu-latest,  rid: linux-x64 }
          - { os: ubuntu-latest,  rid: linux-arm64 }
          - { os: macos-latest,   rid: osx-x64 }
          - { os: macos-latest,   rid: osx-arm64 }
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - run: >
          dotnet publish src/QuickRun.App
          -c Release -r ${{ matrix.rid }}
          --self-contained true
          -p:PublishSingleFile=true
          -o publish/${{ matrix.rid }}
      - uses: actions/upload-artifact@v4
        with:
          name: quickrun-${{ matrix.rid }}
          path: publish/${{ matrix.rid }}
```

Attaching the artifacts to a GitHub Release, checksums, the macOS `.app` bundle and the package
manifests belong to Phase 5 — this workflow only proves that all six targets build.

- [ ] **Step 9: Run everything one last time**

```bash
dotnet build -warnaserror
dotnet test
dotnet run --project src/QuickRun.App -- validate .
dotnet run --project src/QuickRun.App -- --help
```

Expected: build clean, all tests green, QuickRun validates its own config, help lists `run`, `validate`,
`detect`, `ls` and `clean`.

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "docs: samples, README, CI matrix and release build"
```

---

## Self-review

**Spec coverage.** Walking the spec section by section against the tasks:

| Spec section | Covered by |
|---|---|
| §3 architecture, repo layout, subcommands | Tasks 1, 14, 15, 16 (`daemon`, `ui`, `install`, `uninstall` are Phase 2) |
| §4 config, shorthand, platform maps, interpolation, shell selection | Tasks 2, 3, 5 |
| §5 generated input UI | Task 7 (declarations and validation); the rendered form is Phase 2, the console prompt is Task 15 |
| §6 detection fallback, save-as-config | Task 10, wired into `run` in Task 15 |
| §7 git and auth | Task 9 — with two of five credential sources, deliberately and marked |
| §8 workspaces | Task 8 |
| §9 run lifecycle | Tasks 11, 13 |
| §10 security and trust | Task 12 (the auditable plan and its fingerprint) and Task 15 (mandatory confirmation). **The trust store itself is Phase 2** — the CLI confirms every run, which is the strictest behaviour, so nothing is weakened by deferring it |
| §11 trigger transports | Phase 2 and 3, no Phase 1 task |
| §12 extension | Phase 3 |
| §13 site and docs | Phase 4; Task 17 delivers the samples the docs will render and the README |
| §14 distribution | Task 17 builds all six targets; releases and installers are Phase 5 |
| §16 testing | every task, plus Task 17's per-platform sample validation |
| §17 port conflicts | **Gap.** The spec says conflicts are surfaced in the dialog. Phase 1 has no dialog, and `Readiness` treats an already-occupied port as "ready" — which would silently report someone else's server as this repo's. Added below as Task 18 rather than left implicit |

**Placeholder scan.** No `TBD`, no "add error handling", no "similar to Task N". Two places
intentionally describe behaviour in a table rather than full code — `ConfigParser`'s helpers (Task 3
Step 5) and `Runner`'s members (Task 13 Step 3) — because both are long lists of small methods whose
contracts are pinned by the tests immediately above them. Every signature they must satisfy appears in
an **Interfaces** block.

**Type consistency.** `OSKind`/`OSKinds.Current`/`OSKinds.Key()` are used identically from Task 2
onward. `CommandResult`, `ProcessSpec`, `ReadyWhen`, `TaskDef`, `Step`, `InputDef`, `RunPlan`,
`PlannedCommand`, `RunOptions`, `RunEvent`, `WorkspaceInfo`, `Candidate` and `GitOutcome` are declared
once and referenced with the same member names throughout. `ToolChecker.Check` takes
`Func<string, string[], CommandResult>` while `GitClient` takes
`Func<string, string[], string?, CommandResult>` — different arities because git needs a working
directory; both are documented at their declaration.

---

### Task 18: Port-conflict detection

**Files:**
- Modify: `src/QuickRun.Core/Run/Readiness.cs`
- Modify: `src/QuickRun.Core/Run/RunPlan.cs`
- Modify: `src/QuickRun.App/Commands/RunCommand.cs`
- Test: `tests/QuickRun.Core.Tests/PortConflictTests.cs`

**Interfaces:**
- Consumes: `RunConfig`, `TaskDef`, `ReadyWhen`.
- Produces:

```csharp
public sealed record PortConflict(string Task, int Port);

public static class PortScan
{
    public static IReadOnlyList<PortConflict> Occupied(RunConfig config, Func<int, bool>? isInUse = null);
}
```

Closing the gap the self-review found: a `readyWhen: {port: 5000}` task whose port is already taken by
an unrelated process reports ready instantly and the user sees someone else's application. Detecting
this before the run and naming it in the confirmation output costs one TCP connect per declared port.

- [ ] **Step 1: Write the failing test**

`tests/QuickRun.Core.Tests/PortConflictTests.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using QuickRun.Core;
using QuickRun.Core.Config;
using QuickRun.Core.Run;

namespace QuickRun.Core.Tests;

public class PortConflictTests
{
    private static RunConfig Config(string yaml) => ConfigParser.Parse(yaml, OSKind.Linux);

    [Fact]
    public void No_declared_ports_means_no_conflicts()
        => Assert.Empty(PortScan.Occupied(Config("run: ./a"), _ => true));

    [Fact]
    public void A_declared_port_that_is_free_is_not_a_conflict()
        => Assert.Empty(PortScan.Occupied(Config("tasks:\n  - name: api\n    run: a\n    readyWhen: {port: 5000}"),
            _ => false));

    [Fact]
    public void A_declared_port_that_is_taken_is_reported_with_its_task()
    {
        var conflicts = PortScan.Occupied(
            Config("tasks:\n  - name: api\n    run: a\n    readyWhen: {port: 5000}"), _ => true);
        var c = Assert.Single(conflicts);
        Assert.Equal("api", c.Task);
        Assert.Equal(5000, c.Port);
    }

    [Fact]
    public void Only_the_taken_ports_are_reported()
    {
        var yaml = string.Join("\n",
            "tasks:",
            "  - name: free",
            "    run: a",
            "    readyWhen: {port: 5000}",
            "  - name: taken",
            "    run: b",
            "    readyWhen: {port: 5001}");
        Assert.Equal("taken", Assert.Single(PortScan.Occupied(Config(yaml), p => p == 5001)).Task);
    }

    [Fact]
    public void The_real_probe_finds_a_port_this_test_is_listening_on()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            var conflicts = PortScan.Occupied(Config($"tasks:\n  - name: api\n    run: a\n    readyWhen: {{port: {port}}}"));
            Assert.Single(conflicts);
        }
        finally { listener.Stop(); }
    }
}
```

- [ ] **Step 2: Run and watch it fail**

Run: `dotnet test tests/QuickRun.Core.Tests --filter PortConflictTests`
Expected: FAIL — `PortScan` does not exist.

- [ ] **Step 3: Implement `PortScan`**

```csharp
using System.Net.Sockets;
using QuickRun.Core.Config;

namespace QuickRun.Core.Run;

public sealed record PortConflict(string Task, int Port);

public static class PortScan
{
    public static IReadOnlyList<PortConflict> Occupied(RunConfig config, Func<int, bool>? isInUse = null)
    {
        isInUse ??= InUse;
        return config.Tasks
            .Where(t => t.ReadyWhen?.Port is not null)
            .Select(t => new PortConflict(t.Name, t.ReadyWhen!.Port!.Value))
            .Where(c => isInUse(c.Port))
            .ToList();
    }

    private static bool InUse(int port)
    {
        try
        {
            using var client = new TcpClient();
            return client.ConnectAsync("127.0.0.1", port).Wait(300) && client.Connected;
        }
        catch { return false; }
    }
}
```

- [ ] **Step 4: Surface it in the CLI**

In `RunCommand.Execute`, immediately after `Output.Plan(prep.Plan!)` and before the confirmation
prompt:

```csharp
foreach (var conflict in PortScan.Occupied(prep.Config!))
    Output.Warn($"port {conflict.Port} (needed by task '{conflict.Task}') is already in use - " +
                "the readiness check may match another application");
```

A warning, not a block: a user re-running an app they already have open is a normal thing to do, and
QuickRun does not know whose listener it is. Phase 2's dialog renders the same list.

- [ ] **Step 5: Run and watch them pass**

Run: `dotnet test tests/QuickRun.Core.Tests --filter PortConflictTests`
Expected: PASS, 5 tests.

- [ ] **Step 6: Run everything**

Run: `dotnet build -warnaserror && dotnet test`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: warn about ports already in use before a run starts"
```
