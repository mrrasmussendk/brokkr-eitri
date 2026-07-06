# Heimdall NativeAOT Port Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace all Python under `heimdall/` with a NativeAOT-compiled .NET CLI (`src/Heimdall.Cli`, subcommands `hook`/`map`/`drift`/`estimate`) that is byte-compatible with the Python outputs, then delete the Python files.

**Architecture:** `Heimdall.Sensors` (already committed) holds the sensor model and the two ported sensors. `Heimdall.Cli` adds the console entry point, a Python-`json`-compatible serializer (`PyJson`), a per-session state file store (fixing the O(n²) telemetry rescan), and the four subcommands. `TokenEstimator` is already source-linked from `src/Shared/` into Eitri.Analyzers; the CLI links the same file for `heimdall estimate`. Tests drive `HeimdallApp.Run(...)` in-process against temp directories; `heimdall/smoke_test.sh` drives the published AOT binary end-to-end.

**Tech Stack:** .NET 10 (SDK 10.0.204 installed), NativeAOT publish, System.Text.Json source generators (read side only — write side is hand-rolled `PyJson` for byte compatibility), xunit 2.9.2.

## Global Constraints

- `src/Heimdall.Cli`: `PublishAot=true`, `InvariantGlobalization=true`, TargetFramework `net10.0`.
- NO JSON reflection anywhere in the CLI: reads via `JsonSerializerContext` source gen; writes via hand-rolled `PyJson` (byte-compatible with Python `json.dumps` / `json.dump(indent=2)`).
- NO reflection sensor discovery: `SensorRegistry.All` explicit list (already in place).
- Byte-compatible formats: `.heimdall/telemetry.jsonl` (Python compact separators `", "` / `": "`, key order `event, path, kind, slice, feedback, sensor, error, ts, session`, nulls omitted), `.heimdall/map.json` (Python `indent=2`), AGENTS.md `<!--heimdall:deps-->...<!--/heimdall:deps-->` markers.
- The CLI always writes files with LF line endings and UTF-8 no BOM. Parity diffs against Python (which writes CRLF in text mode on Windows) use `diff --strip-trailing-cr`, and `"ts": <float>` is normalized with sed before diffing telemetry.
- Repo root for `hook`/`drift`/`estimate` = current working directory (Claude Code runs hooks with CWD = project root; the Python scripts used their own file location, which the CLI cannot).
- Fix (not replicate): boundary_edits O(n²) telemetry rescan → per-session state file `.heimdall/session-<id>.json` (already wired via `ISessionStore`; this plan implements `FileSessionStore`).
- Keep the Python files until the byte-parity diff is clean; delete them in the same commit as the smoke-test rewrite (Task 6).
- Python interpreter for parity runs on this machine: `py -3` (Python 3.14.0). `python3` is a broken Store alias — never call it.
- Hook latency must measure ≤ 30 ms/event; smoke test prints the measurement (runs in CI).
- Do not restructure anything outside `heimdall/`, `src/Heimdall.*`, `src/Shared`, `tests/`, `.github/`, `.claude/settings.json`, and docs.
- Commit per subcommand. Commit messages end with `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.

## Already done (committed by a previous session — do NOT redo)

- `src/Shared/TokenEstimator.cs` (namespace `Brokkr.Tokenization`, `internal static`), linked into `Eitri.Analyzers.csproj`; `Core.cs` duplicate removed.
- `src/Heimdall.Sensors/` — `Model.cs` (HookEvent/ToolInput/MapModel/SliceInfo/TelemetryRecord/Finding/ISensor/ISessionStore/HeimdallContext), `PathUtil.cs`, `BoundaryReadsSensor.cs`, `BoundaryEditsSensor.cs`, `SensorRegistry.cs`, csproj with `IsAotCompatible`.
- README "Heimdall" section already rewritten (NativeAOT wording, class+registry sensor instructions).

## File Structure (this plan creates/modifies)

- Create: `src/Heimdall.Cli/Heimdall.Cli.csproj` — exe, AOT, links TokenEstimator, refs Sensors
- Create: `src/Heimdall.Cli/Program.cs` — UTF-8 stream wiring, calls HeimdallApp
- Create: `src/Heimdall.Cli/HeimdallApp.cs` — subcommand dispatch (testable entry)
- Create: `src/Heimdall.Cli/PyJson.cs` — Python-json-compatible writer (telemetry lines + map document)
- Create: `src/Heimdall.Cli/HeimdallJsonContext.cs` — STJ source-gen context + SessionState
- Create: `src/Heimdall.Cli/FileSessionStore.cs` — `.heimdall/session-<id>.json`
- Create: `src/Heimdall.Cli/EstimateCommand.cs`, `MapCommand.cs`, `HookCommand.cs`, `DriftCommand.cs`
- Create: `tests/Heimdall.Tests/Heimdall.Tests.csproj`, `TempRepo.cs`, `FormatTests.cs`, `BehavioralScenarios.cs`
- Modify: `heimdall/smoke_test.sh` (drive AOT binary, add estimate + latency checks)
- Delete: `heimdall/heimdall.py`, `heimdall/emit_map.py`, `heimdall/drift_report.py`, `heimdall/sensors/` (incl. `__pycache__`), `tools/calibrate.py`
- Create: `docs/calibration.md` (tiktoken calibration doc — the only intentional "python" mention outside CI)
- Modify: `.claude/settings.json`, `CONTRIBUTING.md`, `README.md` (small additions), `.github/workflows/ci.yml`

## Interfaces shared across tasks

- `internal static class HeimdallApp { public static int Run(string[] args, TextReader stdin, TextWriter stdout, TextWriter stderr, string root) }` — root = repo root (CWD in production, temp dir in tests). Exit codes: 0 ok/silent, 1 hard error (message on stderr), 2 usage error or hook feedback.
- `internal static class PyJson { static string Str(string s); static string Float(double d); static string Line(Finding f); static string MapDocument(string kernel, string slicesDir, IReadOnlyList<MapSlice> slices) }` with `internal readonly record struct MapSlice(string Name, string Path, IReadOnlyList<string> DependsOn, int Budget, int FanIn)`.
- `internal sealed class FileSessionStore(string heimdallDir) : ISessionStore`.
- `public sealed class SessionState { List<string> EditedSlices }` with `[JsonPropertyName("edited_slices")]`.
- Tests access internals via `<InternalsVisibleTo Include="Heimdall.Tests" />` in Heimdall.Cli.csproj.

---

### Task 1: Heimdall.Cli skeleton + PyJson + `estimate` subcommand

**Files:**
- Create: `src/Heimdall.Cli/Heimdall.Cli.csproj`
- Create: `src/Heimdall.Cli/Program.cs`
- Create: `src/Heimdall.Cli/HeimdallApp.cs`
- Create: `src/Heimdall.Cli/PyJson.cs`
- Create: `src/Heimdall.Cli/EstimateCommand.cs`
- Create: `tests/Heimdall.Tests/Heimdall.Tests.csproj`
- Create: `tests/Heimdall.Tests/TempRepo.cs`
- Test: `tests/Heimdall.Tests/FormatTests.cs`

**Interfaces:**
- Consumes: `Brokkr.Tokenization.TokenEstimator.Estimate(string)` (linked source), `Heimdall.Sensors.Finding` (public fields Event/Path/Kind/Slice/Feedback/Sensor/Error/Ts/Session).
- Produces: `HeimdallApp.Run` (signature above — every later task adds a case to its switch), `PyJson.Str/Float/Line/MapDocument`, `MapSlice`, `TempRepo` test helper.

- [ ] **Step 1: Create the two csproj files and stub source so the test project compiles**

`src/Heimdall.Cli/Heimdall.Cli.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Heimdall.Cli</RootNamespace>
    <AssemblyName>heimdall</AssemblyName>
    <!-- Heimdall runs as a PostToolUse hook — a fresh process on EVERY tool call.
         NativeAOT keeps cold start in single-digit milliseconds; JIT startup would not. -->
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>true</InvariantGlobalization>
    <StripSymbols>true</StripSymbols>
    <!-- Installable as a dotnet tool: dotnet tool install --global Heimdall.Cli -->
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>heimdall</ToolCommandName>
    <PackageId>Heimdall.Cli</PackageId>
    <Version>0.1.0</Version>
    <IsPackable>true</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <!-- Single source of truth for token counting, shared with Eitri.Analyzers. -->
    <Compile Include="..\Shared\TokenEstimator.cs" Link="Shared\TokenEstimator.cs" />
    <ProjectReference Include="..\Heimdall.Sensors\Heimdall.Sensors.csproj" />
    <InternalsVisibleTo Include="Heimdall.Tests" />
  </ItemGroup>
</Project>
```

`tests/Heimdall.Tests/Heimdall.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" PrivateAssets="all" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Heimdall.Cli\Heimdall.Cli.csproj" />
  </ItemGroup>
</Project>
```

`src/Heimdall.Cli/Program.cs`:

```csharp
using System.Text;
using Heimdall.Cli;

// Byte-stable I/O regardless of console code page: stdin/stdout/stderr are UTF-8.
// (Feedback text contains em dashes; Windows OEM code pages would mangle them.)
var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
using var stdin = new StreamReader(Console.OpenStandardInput(), utf8);
using var stdout = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true };
using var stderr = new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true };
return HeimdallApp.Run(args, stdin, stdout, stderr, Environment.CurrentDirectory);
```

`src/Heimdall.Cli/HeimdallApp.cs` (Task 1 version — later tasks replace the `not yet ported` cases):

```csharp
namespace Heimdall.Cli;

/// <summary>Testable entry point: Program.cs passes the real console streams and CWD.</summary>
internal static class HeimdallApp
{
    private const string Usage =
        "usage: heimdall <hook|map|drift|estimate> [args]\n" +
        "  hook                      read a PostToolUse event on stdin, run sensors, exit 2 on feedback\n" +
        "  map --root <dir> [--budget 15000] [--kernel SharedKernel]\n" +
        "  drift                     telemetry vs map: per-session table, then per-slice aggregate\n" +
        "  estimate <file-or-dir>    token estimate (same estimator Eitri compiles with)";

    public static int Run(string[] args, TextReader stdin, TextWriter stdout, TextWriter stderr, string root)
    {
        if (args.Length == 0) { stderr.WriteLine(Usage); return 2; }
        switch (args[0])
        {
            case "estimate": return EstimateCommand.Run(args[1..], stdout, stderr, root);
            case "hook": case "map": case "drift":
                stderr.WriteLine($"heimdall {args[0]}: not yet ported"); return 1;
            default:
                stderr.WriteLine(Usage); return 2;
        }
    }
}
```

`src/Heimdall.Cli/EstimateCommand.cs`:

```csharp
using System.Globalization;
using Brokkr.Tokenization;

namespace Heimdall.Cli;

/// <summary>
/// `heimdall estimate <file-or-dir>` — prints the token estimate as a bare integer.
/// Directories walk *.cs recursively (what EIT100 budgets). Same linked TokenEstimator
/// source as Eitri.Analyzers, so the number cannot drift from the compile-time gate.
/// </summary>
internal static class EstimateCommand
{
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, string cwd)
    {
        if (args.Length != 1) { stderr.WriteLine("usage: heimdall estimate <file-or-dir>"); return 2; }
        var target = Path.GetFullPath(args[0], cwd);
        if (File.Exists(target))
        {
            stdout.WriteLine(TokenEstimator.Estimate(File.ReadAllText(target)).ToString(CultureInfo.InvariantCulture));
            return 0;
        }
        if (Directory.Exists(target))
        {
            long total = 0;
            foreach (var f in Directory.EnumerateFiles(target, "*.cs", SearchOption.AllDirectories))
                total += TokenEstimator.Estimate(File.ReadAllText(f));
            stdout.WriteLine(total.ToString(CultureInfo.InvariantCulture));
            return 0;
        }
        stderr.WriteLine($"heimdall estimate: no such file or directory: {args[0]}");
        return 1;
    }
}
```

`src/Heimdall.Cli/PyJson.cs`:

```csharp
using System.Globalization;
using System.Text;
using Heimdall.Sensors;

namespace Heimdall.Cli;

internal readonly record struct MapSlice(string Name, string Path, IReadOnlyList<string> DependsOn, int Budget, int FanIn);

/// <summary>
/// Serializes exactly like Python's json module — the byte-compatibility contract for
/// .heimdall/telemetry.jsonl and .heimdall/map.json. Telemetry lines use json.dumps
/// defaults (", " / ": " separators, ensure_ascii); the map uses json.dump(indent=2).
/// Finding key order mirrors Python dict insertion order across all producers:
/// event, path, kind, slice, feedback, sensor, error, ts, session (nulls omitted).
/// </summary>
internal static class PyJson
{
    public static string Str(string s)
    {
        var sb = new StringBuilder(s.Length + 2).Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    // ensure_ascii=True: everything outside printable ASCII becomes \uXXXX
                    // (a surrogate pair becomes two escapes, exactly like CPython).
                    if (c < 0x20 || c > 0x7E)
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    /// <summary>Python repr() floats: shortest round-trip, and integral values keep a ".0".</summary>
    public static string Float(double d)
    {
        var s = d.ToString("R", CultureInfo.InvariantCulture);
        return s.Contains('.') || s.Contains('e') || s.Contains('E') ? s : s + ".0";
    }

    public static string Line(Finding f)
    {
        var sb = new StringBuilder("{");
        var first = true;
        void Emit(string key, string value)
        {
            if (!first) sb.Append(", ");
            first = false;
            sb.Append('"').Append(key).Append("\": ").Append(value);
        }
        if (f.Event is not null) Emit("event", Str(f.Event));
        if (f.Path is not null) Emit("path", Str(f.Path));
        if (f.Kind is not null) Emit("kind", Str(f.Kind));
        if (f.Slice is not null) Emit("slice", Str(f.Slice));
        if (f.Feedback is not null) Emit("feedback", Str(f.Feedback));
        if (f.Sensor is not null) Emit("sensor", Str(f.Sensor));
        if (f.Error is not null) Emit("error", Str(f.Error));
        Emit("ts", Float(f.Ts));
        if (f.Session is not null) Emit("session", Str(f.Session));
        return sb.Append('}').ToString();
    }

    public static string MapDocument(string kernel, string slicesDir, IReadOnlyList<MapSlice> slices)
    {
        var sb = new StringBuilder("{\n");
        sb.Append("  \"kernel\": ").Append(Str(kernel)).Append(",\n");
        sb.Append("  \"slices_dir\": ").Append(Str(slicesDir)).Append(",\n");
        if (slices.Count == 0) return sb.Append("  \"slices\": {}\n}").ToString();
        sb.Append("  \"slices\": {\n");
        for (var i = 0; i < slices.Count; i++)
        {
            var s = slices[i];
            sb.Append("    ").Append(Str(s.Name)).Append(": {\n");
            sb.Append("      \"path\": ").Append(Str(s.Path)).Append(",\n");
            if (s.DependsOn.Count == 0) sb.Append("      \"depends_on\": [],\n");
            else
            {
                sb.Append("      \"depends_on\": [\n");
                for (var j = 0; j < s.DependsOn.Count; j++)
                    sb.Append("        ").Append(Str(s.DependsOn[j])).Append(j < s.DependsOn.Count - 1 ? ",\n" : "\n");
                sb.Append("      ],\n");
            }
            sb.Append("      \"budget\": ").Append(s.Budget.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("      \"fan_in\": ").Append(s.FanIn.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("    }").Append(i < slices.Count - 1 ? ",\n" : "\n");
        }
        return sb.Append("  }\n}").ToString();
    }
}
```

`tests/Heimdall.Tests/TempRepo.cs`:

```csharp
using System.Text;
using Heimdall.Cli;

namespace Heimdall.Tests;

/// <summary>A throwaway repo root that HeimdallApp.Run treats as CWD.</summary>
internal sealed class TempRepo : IDisposable
{
    public string Root { get; } =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "heimdall-tests-" + Guid.NewGuid().ToString("N"))).FullName;

    public void WriteFile(string rel, string content)
    {
        var p = Path.Combine(Root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content, new UTF8Encoding(false));
    }

    public string ReadFile(string rel) => File.ReadAllText(Path.Combine(Root, rel));
    public bool HasFile(string rel) => File.Exists(Path.Combine(Root, rel));
    public string Telemetry => HasFile(".heimdall/telemetry.jsonl") ? ReadFile(".heimdall/telemetry.jsonl") : "";

    public (int Code, string Stdout, string Stderr) Run(string stdinText, params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = HeimdallApp.Run(args, new StringReader(stdinText), stdout, stderr, Root);
        return (code, stdout.ToString(), stderr.ToString());
    }

    public (int Code, string Stderr) Hook(string eventJson)
    {
        var r = Run(eventJson, "hook");
        return (r.Code, r.Stderr);
    }

    /// <summary>A PostToolUse event exactly as the smoke test forges them.</summary>
    public static string Ev(string session, string tool, string path) =>
        $"{{\"session_id\":\"{session}\",\"tool_name\":\"{tool}\",\"tool_input\":{{\"file_path\":\"{path}\"}}}}";

    /// <summary>The samples-shaped map: Domme depends on Retskilder; optional frozen high fan-in slice.</summary>
    public void WriteSampleMap(bool withFrozenCore = false)
    {
        var core = withFrozenCore
            ? ",\n    \"Core\": {\n      \"path\": \"samples/Slices/Core\",\n      \"depends_on\": [],\n      \"budget\": 15000,\n      \"fan_in\": 12\n    }"
            : "";
        WriteFile(".heimdall/map.json",
            "{\n  \"kernel\": \"SharedKernel\",\n  \"slices_dir\": \"samples/Slices\",\n  \"slices\": {\n" +
            "    \"Domme\": {\n      \"path\": \"samples/Slices/Domme\",\n      \"depends_on\": [\n        \"Retskilder\"\n      ],\n      \"budget\": 15000,\n      \"fan_in\": 0\n    },\n" +
            "    \"Retskilder\": {\n      \"path\": \"samples/Slices/Retskilder\",\n      \"depends_on\": [],\n      \"budget\": 15000,\n      \"fan_in\": 1\n    }" +
            core + "\n  }\n}");
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, true); } catch { /* Windows file locks — temp dir, best effort */ }
    }
}
```

- [ ] **Step 2: Write the failing format tests**

`tests/Heimdall.Tests/FormatTests.cs`:

```csharp
using Heimdall.Cli;
using Heimdall.Sensors;
using Xunit;

namespace Heimdall.Tests;

public class FormatTests
{
    [Fact]
    public void PyJson_Str_EscapesLikeCPython()
    {
        Assert.Equal("\"a\\\"b\\\\c\"", PyJson.Str("a\"b\\c"));
        Assert.Equal("\"tab\\there\"", PyJson.Str("tab\there"));
        Assert.Equal("\"\\u2014\"", PyJson.Str("—"));           // em dash -> ensure_ascii
        Assert.Equal("\"\\u0001\"", PyJson.Str(""));
        Assert.Equal("\"\\ud83d\\ude00\"", PyJson.Str("\U0001F600")); // surrogate pair, two escapes
    }

    [Fact]
    public void PyJson_Float_MatchesPythonRepr()
    {
        Assert.Equal("1751812345.6789093", PyJson.Float(1751812345.6789093));
        Assert.Equal("1751812345.0", PyJson.Float(1751812345.0));
    }

    [Fact]
    public void PyJson_Line_UsesPythonSeparatorsAndKeyOrder()
    {
        var f = new Finding { Event = "read", Path = "a/b.cs", Kind = "slice:Domme", Sensor = "boundary_reads", Ts = 1751812345.5, Session = "s1" };
        Assert.Equal(
            "{\"event\": \"read\", \"path\": \"a/b.cs\", \"kind\": \"slice:Domme\", \"sensor\": \"boundary_reads\", \"ts\": 1751812345.5, \"session\": \"s1\"}",
            PyJson.Line(f));
        var err = new Finding { Sensor = "heimdall", Error = "boom", Ts = 2.0 };
        Assert.Equal("{\"sensor\": \"heimdall\", \"error\": \"boom\", \"ts\": 2.0}", PyJson.Line(err));
    }

    [Fact]
    public void PyJson_MapDocument_MatchesPythonIndent2()
    {
        var doc = PyJson.MapDocument("SharedKernel", "samples\\Slices", new[]
        {
            new MapSlice("Domme", "samples\\Slices\\Domme", new[] { "Retskilder" }, 15000, 0),
            new MapSlice("Retskilder", "samples\\Slices\\Retskilder", Array.Empty<string>(), 15000, 1),
        });
        Assert.Equal(
            "{\n  \"kernel\": \"SharedKernel\",\n  \"slices_dir\": \"samples\\\\Slices\",\n  \"slices\": {\n" +
            "    \"Domme\": {\n      \"path\": \"samples\\\\Slices\\\\Domme\",\n      \"depends_on\": [\n        \"Retskilder\"\n      ],\n      \"budget\": 15000,\n      \"fan_in\": 0\n    },\n" +
            "    \"Retskilder\": {\n      \"path\": \"samples\\\\Slices\\\\Retskilder\",\n      \"depends_on\": [],\n      \"budget\": 15000,\n      \"fan_in\": 1\n    }\n" +
            "  }\n}",
            doc);
    }

    [Fact]
    public void Estimate_File_MatchesLinkedEstimator()
    {
        using var repo = new TempRepo();
        const string src = "namespace X;\npublic sealed class Thing { public int Answer() => 42; }\n";
        repo.WriteFile("samples/Thing.cs", src);
        var r = repo.Run("", "estimate", "samples/Thing.cs");
        Assert.Equal(0, r.Code);
        Assert.Equal(Brokkr.Tokenization.TokenEstimator.Estimate(src).ToString(), r.Stdout.Trim());
    }

    [Fact]
    public void Estimate_Directory_SumsAllCsFiles()
    {
        using var repo = new TempRepo();
        repo.WriteFile("tree/A.cs", "class A { }");
        repo.WriteFile("tree/sub/B.cs", "class B { int x = 123; }");
        repo.WriteFile("tree/ignored.txt", "not counted");
        var expected = Brokkr.Tokenization.TokenEstimator.Estimate("class A { }")
                     + Brokkr.Tokenization.TokenEstimator.Estimate("class B { int x = 123; }");
        var r = repo.Run("", "estimate", "tree");
        Assert.Equal(0, r.Code);
        Assert.Equal(expected.ToString(), r.Stdout.Trim());
    }

    [Fact]
    public void Estimate_MissingPath_Exit1()
    {
        using var repo = new TempRepo();
        var r = repo.Run("", "estimate", "nope.cs");
        Assert.Equal(1, r.Code);
        Assert.Contains("no such file", r.Stderr);
    }
}
```

- [ ] **Step 3: Run the tests — expect them to FAIL only if the code is wrong, so first verify a clean build**

Run: `dotnet test tests/Heimdall.Tests -c Release`
Expected: all FormatTests PASS (code and tests land together in this task; the failing-first cycle applies from Task 2 on where behavior is subtler). If `TokenEstimator` is inaccessible, the linked `Compile Include` path is wrong — it must be `..\Shared\TokenEstimator.cs` relative to `src/Heimdall.Cli/`.

- [ ] **Step 4: Verify NativeAOT publish is clean**

Run: `dotnet publish src/Heimdall.Cli -c Release -r win-x64`
Expected: succeeds with zero AOT/trim warnings; binary at `src/Heimdall.Cli/bin/Release/net10.0/win-x64/publish/heimdall.exe`.
Run: `src/Heimdall.Cli/bin/Release/net10.0/win-x64/publish/heimdall.exe estimate samples` → prints a positive integer.

- [ ] **Step 5: Commit**

```bash
git add src/Heimdall.Cli tests/Heimdall.Tests
git commit -m "Heimdall.Cli: NativeAOT skeleton + PyJson (Python-json byte compatibility) + estimate subcommand

estimate shares src/Shared/TokenEstimator.cs with Eitri.Analyzers by source link,
so the harness and the compile-time budget gate can never drift.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: `map` subcommand (port of emit_map.py)

**Files:**
- Create: `src/Heimdall.Cli/MapCommand.cs`
- Modify: `src/Heimdall.Cli/HeimdallApp.cs` (replace the `map` case)
- Test: `tests/Heimdall.Tests/MapCommandTests.cs`

**Interfaces:**
- Consumes: `PyJson.MapDocument`, `MapSlice`.
- Produces: `MapCommand.Run(string[] args, TextWriter stdout, TextWriter stderr, string cwd)` returning int. Writes `.heimdall/map.json` **relative to cwd** (exactly like the Python, which wrote relative to the invoking CWD) and rewrites each slice's AGENTS.md.

**Faithful-port notes (from emit_map.py, verified against source):**
- slices dir search order: `<root>/Slices`, then `<root>/src/Slices`; neither → stderr `no Slices/ under {root}` (root verbatim as passed), exit 1.
- slice discovery: sorted directory listing (ordinal), keep `n` only if `<slicesDir>/n/n.csproj` exists.
- dependency regex is `\.\.\\(\w+)\\\w+\.csproj` — backslash-only, single `..\` hop, so the kernel's `..\..\SharedKernel\...` never matches. Dedupe, drop kernel, sort ordinal.
- AGENTS.md: replace ALL `<!--heimdall:deps-->.*?<!--/heimdall:deps-->` (singleline) occurrences; else append `\n` + depLine + `\n` after TrimEnd(); missing file → `# {n}\n{depLine}\n`. Use a MatchEvaluator so `$` in content can't corrupt the replacement.
- paths in map.json are `Path.GetRelativePath(cwd, ...)` — native separators, matching `os.path.relpath` on the same OS.
- stdout on success: `heimdall map: {N} slices -> .heimdall/map.json; AGENTS.md deps regenerated`.

- [ ] **Step 1: Write the failing tests**

`tests/Heimdall.Tests/MapCommandTests.cs`:

```csharp
using Xunit;

namespace Heimdall.Tests;

public class MapCommandTests
{
    private static void WriteSampleTree(TempRepo repo)
    {
        repo.WriteFile("samples/Slices/Domme/Domme.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n" +
            "    <ProjectReference Include=\"..\\..\\SharedKernel\\SharedKernel.csproj\" />\n" +
            "    <ProjectReference Include=\"..\\Retskilder\\Retskilder.csproj\" />\n" +
            "  </ItemGroup>\n</Project>\n");
        repo.WriteFile("samples/Slices/Retskilder/Retskilder.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n" +
            "    <ProjectReference Include=\"..\\..\\SharedKernel\\SharedKernel.csproj\" />\n" +
            "  </ItemGroup>\n</Project>\n");
        repo.WriteFile("samples/Slices/Domme/AGENTS.md", "# Domme\nhand-written notes stay.\n");
    }

    [Fact]
    public void Map_EmitsPythonCompatibleMapJson()
    {
        using var repo = new TempRepo();
        WriteSampleTree(repo);
        var r = repo.Run("", "map", "--root", "samples", "--budget", "15000");
        Assert.Equal(0, r.Code);
        Assert.Contains("heimdall map: 2 slices -> .heimdall/map.json; AGENTS.md deps regenerated", r.Stdout);
        var sep = Path.DirectorySeparatorChar == '\\' ? "\\\\" : "/";
        Assert.Equal(
            "{\n  \"kernel\": \"SharedKernel\",\n  \"slices_dir\": \"samples" + sep + "Slices\",\n  \"slices\": {\n" +
            "    \"Domme\": {\n      \"path\": \"samples" + sep + "Slices" + sep + "Domme\",\n      \"depends_on\": [\n        \"Retskilder\"\n      ],\n      \"budget\": 15000,\n      \"fan_in\": 0\n    },\n" +
            "    \"Retskilder\": {\n      \"path\": \"samples" + sep + "Slices" + sep + "Retskilder\",\n      \"depends_on\": [],\n      \"budget\": 15000,\n      \"fan_in\": 1\n    }\n" +
            "  }\n}",
            repo.ReadFile(".heimdall/map.json"));
    }

    [Fact]
    public void Map_InjectsAndRefreshesAgentsMdMarkers_Idempotently()
    {
        using var repo = new TempRepo();
        WriteSampleTree(repo);
        repo.Run("", "map", "--root", "samples");
        Assert.Equal("# Domme\nhand-written notes stay.\n<!--heimdall:deps-->depends on: Retskilder + SharedKernel<!--/heimdall:deps-->\n",
            repo.ReadFile("samples/Slices/Domme/AGENTS.md"));
        // Retskilder had no AGENTS.md -> created from scratch
        Assert.Equal("# Retskilder\n<!--heimdall:deps-->depends on: (none) + SharedKernel<!--/heimdall:deps-->\n",
            repo.ReadFile("samples/Slices/Retskilder/AGENTS.md"));
        // rerun: markers replaced in place, no duplication
        repo.Run("", "map", "--root", "samples");
        Assert.Equal("# Domme\nhand-written notes stay.\n<!--heimdall:deps-->depends on: Retskilder + SharedKernel<!--/heimdall:deps-->\n",
            repo.ReadFile("samples/Slices/Domme/AGENTS.md"));
    }

    [Fact]
    public void Map_HonorsBudgetAndKernelFlags()
    {
        using var repo = new TempRepo();
        WriteSampleTree(repo);
        repo.Run("", "map", "--root", "samples", "--budget", "9000", "--kernel", "Retskilder");
        var map = repo.ReadFile(".heimdall/map.json");
        Assert.Contains("\"budget\": 9000", map);
        Assert.Contains("\"kernel\": \"Retskilder\"", map);
        // Retskilder is now the kernel -> dropped from Domme's deps
        Assert.Contains("\"depends_on\": [],", map);
    }

    [Fact]
    public void Map_NoSlicesDir_Exit1WithPythonMessage()
    {
        using var repo = new TempRepo();
        var r = repo.Run("", "map", "--root", "nowhere");
        Assert.Equal(1, r.Code);
        Assert.Contains("no Slices/ under nowhere", r.Stderr);
    }
}
```

- [ ] **Step 2: Run tests to verify the new ones fail**

Run: `dotnet test tests/Heimdall.Tests -c Release --filter MapCommandTests`
Expected: FAIL — `heimdall map: not yet ported` (exit 1) from the Task 1 stub.

- [ ] **Step 3: Implement MapCommand and wire the dispatch**

`src/Heimdall.Cli/MapCommand.cs`:

```csharp
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Heimdall.Cli;

/// <summary>
/// `heimdall map --root <dir> [--budget 15000] [--kernel SharedKernel]` — the feedforward
/// emitter (port of emit_map.py). The csproj graph is the ground truth: writes
/// .heimdall/map.json (relative to CWD) and regenerates the AGENTS.md deps markers.
/// </summary>
internal static partial class MapCommand
{
    [GeneratedRegex(@"\.\.\\(\w+)\\\w+\.csproj")]
    private static partial Regex RefRegex();

    [GeneratedRegex("<!--heimdall:deps-->.*?<!--/heimdall:deps-->", RegexOptions.Singleline)]
    private static partial Regex DepsRegex();

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, string cwd)
    {
        string? root = null;
        var budget = 15000;
        var kernel = "SharedKernel";
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--root" when i + 1 < args.Length: root = args[++i]; break;
                case "--budget" when i + 1 < args.Length: budget = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--kernel" when i + 1 < args.Length: kernel = args[++i]; break;
                default:
                    stderr.WriteLine("usage: heimdall map --root <dir> [--budget 15000] [--kernel SharedKernel]");
                    return 2;
            }
        }
        if (root is null)
        {
            stderr.WriteLine("usage: heimdall map --root <dir> [--budget 15000] [--kernel SharedKernel]");
            return 2;
        }

        var rootAbs = Path.GetFullPath(root, cwd);
        string? slicesDir = null;
        foreach (var cand in new[] { Path.Combine(rootAbs, "Slices"), Path.Combine(rootAbs, "src", "Slices") })
            if (Directory.Exists(cand)) { slicesDir = cand; break; }
        if (slicesDir is null) { stderr.WriteLine($"no Slices/ under {root}"); return 1; }

        var names = Directory.EnumerateFileSystemEntries(slicesDir)
            .Select(Path.GetFileName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var discovered = new List<(string Name, List<string> DependsOn)>();
        foreach (var name in names)
        {
            if (name is null) continue;
            var csproj = Path.Combine(slicesDir, name, name + ".csproj");
            if (!File.Exists(csproj)) continue;
            var refs = RefRegex().Matches(File.ReadAllText(csproj))
                .Select(m => m.Groups[1].Value)
                .Where(r => r != kernel)
                .Distinct()
                .OrderBy(r => r, StringComparer.Ordinal)
                .ToList();
            discovered.Add((name, refs));

            var agents = Path.Combine(slicesDir, name, "AGENTS.md");
            var depLine = $"<!--heimdall:deps-->depends on: {(refs.Count > 0 ? string.Join(", ", refs) : "(none)")} + {kernel}<!--/heimdall:deps-->";
            string txt;
            if (File.Exists(agents))
            {
                txt = File.ReadAllText(agents);
                txt = txt.Contains("<!--heimdall:deps-->", StringComparison.Ordinal)
                    ? DepsRegex().Replace(txt, _ => depLine)
                    : txt.TrimEnd() + "\n" + depLine + "\n";
            }
            else
            {
                txt = $"# {name}\n{depLine}\n";
            }
            File.WriteAllText(agents, txt, new UTF8Encoding(false));
        }

        var fanIn = discovered.ToDictionary(s => s.Name, _ => 0, StringComparer.Ordinal);
        foreach (var (_, deps) in discovered)
            foreach (var d in deps)
                if (fanIn.ContainsKey(d)) fanIn[d]++;

        var relSlicesDir = Path.GetRelativePath(cwd, slicesDir);
        var slices = discovered
            .Select(s => new MapSlice(s.Name, Path.GetRelativePath(cwd, Path.Combine(slicesDir, s.Name)), s.DependsOn, budget, fanIn[s.Name]))
            .ToList();

        Directory.CreateDirectory(Path.Combine(cwd, ".heimdall"));
        File.WriteAllText(Path.Combine(cwd, ".heimdall", "map.json"),
            PyJson.MapDocument(kernel, relSlicesDir, slices), new UTF8Encoding(false));
        stdout.WriteLine($"heimdall map: {slices.Count} slices -> .heimdall/map.json; AGENTS.md deps regenerated");
        return 0;
    }
}
```

In `HeimdallApp.cs`, replace the combined stub case with:

```csharp
            case "map": return MapCommand.Run(args[1..], stdout, stderr, root);
            case "hook": case "drift":
                stderr.WriteLine($"heimdall {args[0]}: not yet ported"); return 1;
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Heimdall.Tests -c Release`
Expected: PASS (all).

- [ ] **Step 5: Byte-parity check against the Python emit_map (from repo root, Git Bash)**

```bash
cd /c/Users/MCSN/RiderProjects/brokkr-eitri
rm -rf .heimdall && git checkout -- samples
py -3 heimdall/emit_map.py --root samples --budget 15000
cp .heimdall/map.json /tmp/py-map.json && cp samples/Slices/Domme/AGENTS.md /tmp/py-agents.md
rm -rf .heimdall && git checkout -- samples
dotnet publish src/Heimdall.Cli -c Release -r win-x64
src/Heimdall.Cli/bin/Release/net10.0/win-x64/publish/heimdall.exe map --root samples --budget 15000
diff --strip-trailing-cr /tmp/py-map.json .heimdall/map.json && echo MAP-PARITY-OK
diff --strip-trailing-cr /tmp/py-agents.md samples/Slices/Domme/AGENTS.md && echo AGENTS-PARITY-OK
git checkout -- samples && rm -rf .heimdall
```

Expected: both `*-PARITY-OK` lines print. (`--strip-trailing-cr` because Python text mode writes CRLF on Windows; the CLI standardizes on LF.)

- [ ] **Step 6: Commit**

```bash
git add src/Heimdall.Cli tests/Heimdall.Tests
git commit -m "Heimdall.Cli: map subcommand — byte-compatible port of emit_map.py"
```
(with the Co-Authored-By trailer)

---

### Task 3: `hook` subcommand + FileSessionStore (port of heimdall.py)

**Files:**
- Create: `src/Heimdall.Cli/HeimdallJsonContext.cs`
- Create: `src/Heimdall.Cli/FileSessionStore.cs`
- Create: `src/Heimdall.Cli/HookCommand.cs`
- Modify: `src/Heimdall.Cli/HeimdallApp.cs` (replace the `hook` case)
- Test: `tests/Heimdall.Tests/HookCommandTests.cs`

**Interfaces:**
- Consumes: `SensorRegistry.All`, `HeimdallContext`, `Finding`, `PyJson.Line`, `HookEvent`/`MapModel` (Sensors project), `ISessionStore`.
- Produces: `HookCommand.Run(TextReader stdin, TextWriter stderr, string root)` → int; `FileSessionStore(string heimdallDir) : ISessionStore`; `SessionState` (JSON: `{"edited_slices": [...]}`); `HeimdallJsonContext` (source-gen context, reused by Task 4 — it must already include `TelemetryRecord`).

**Faithful-port notes (from heimdall.py, verified against source):**
- unparseable stdin → exit 0, no output (Python `except: return 0`).
- map read from `<root>/.heimdall/map.json`; absent → sensors get `Map = null` and stay silent. Invalid map JSON → stderr message, exit 1 (Python crashed with a traceback + exit 1; we keep the exit code, humanize the message).
- runner stamps each finding: `sensor` (only if unset), `ts` (time.time() = Unix seconds as double), `session` (`session_id` or `"?"`). Sensor exception → `{"sensor": "heimdall", "error": msg, "ts": ...}` with NO session key.
- telemetry: append-only, one `PyJson.Line` + `"\n"` per finding; `.heimdall/` created only when there are findings.
- feedback → stderr `"Heimdall: " + " | ".join(feedback)`, exit 2; else exit 0.
- The O(n²) fix: `BoundaryEditsSensor` (already written) calls `ctx.Sessions.GetEditedSlices/AddEditedSlice`; `FileSessionStore` backs those with `.heimdall/session-<sanitized-id>.json`.

- [ ] **Step 1: Write the failing tests**

`tests/Heimdall.Tests/HookCommandTests.cs`:

```csharp
using System.Text.RegularExpressions;
using Xunit;

namespace Heimdall.Tests;

public class HookCommandTests
{
    [Fact]
    public void Hook_ReadInSlice_LogsPythonCompatibleTelemetryLine()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap();
        var (code, stderr) = repo.Hook(TempRepo.Ev("s1", "Read", "samples/Slices/Domme/Internal/DommeEngine.cs"));
        Assert.Equal(0, code);
        Assert.Equal("", stderr);
        Assert.Matches(
            new Regex("^\\{\"event\": \"read\", \"path\": \"samples/Slices/Domme/Internal/DommeEngine\\.cs\", " +
                      "\"kind\": \"slice:Domme\", \"sensor\": \"boundary_reads\", \"ts\": \\d+\\.\\d+, \"session\": \"s1\"\\}\n$"),
            repo.Telemetry);
    }

    [Fact]
    public void Hook_SecondSliceEdit_Exit2WithFeedbackAndSessionStateFile()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap();
        var first = repo.Hook(TempRepo.Ev("s1", "Edit", "samples/Slices/Domme/Internal/DommeEngine.cs"));
        Assert.Equal(0, first.Code);
        Assert.True(repo.HasFile(".heimdall/session-s1.json"));
        var second = repo.Hook(TempRepo.Ev("s1", "Edit", "samples/Slices/Retskilder/Internal/RetskilderEngine.cs"));
        Assert.Equal(2, second.Code);
        Assert.Contains("Heimdall: you are now editing slice 'Retskilder' after editing ['Domme']", second.Stderr);
        Assert.Contains("cross-slice", second.Stderr);
    }

    [Fact]
    public void Hook_InvalidJson_Exit0Silent()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap();
        var r = repo.Run("this is not json", "hook");
        Assert.Equal(0, r.Code);
        Assert.Equal("", r.Stderr);
        Assert.Equal("", repo.Telemetry);
    }

    [Fact]
    public void Hook_NoMap_Exit0NoTelemetry()
    {
        using var repo = new TempRepo();
        var (code, stderr) = repo.Hook(TempRepo.Ev("s1", "Edit", "samples/Slices/Domme/Internal/DommeEngine.cs"));
        Assert.Equal(0, code);
        Assert.Equal("", stderr);
        Assert.False(repo.HasFile(".heimdall/telemetry.jsonl"));
    }

    [Fact]
    public void SessionStore_IsolatesSessions_AndSanitizesIds()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap();
        repo.Hook(TempRepo.Ev("a/b:c", "Edit", "samples/Slices/Domme/Internal/DommeEngine.cs"));
        Assert.True(repo.HasFile(".heimdall/session-a_b_c.json"));
        Assert.Contains("\"edited_slices\":[\"Domme\"]", repo.ReadFile(".heimdall/session-a_b_c.json").Replace(" ", ""));
        // a different session editing another slice gets no cross-slice warning
        var other = repo.Hook(TempRepo.Ev("other", "Edit", "samples/Slices/Retskilder/Internal/RetskilderEngine.cs"));
        Assert.Equal(0, other.Code);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Heimdall.Tests -c Release --filter HookCommandTests`
Expected: FAIL — `heimdall hook: not yet ported`.

- [ ] **Step 3: Implement**

`src/Heimdall.Cli/HeimdallJsonContext.cs`:

```csharp
using System.Text.Json.Serialization;
using Heimdall.Sensors;

namespace Heimdall.Cli;

/// <summary>Per-session edit history persisted at .heimdall/session-&lt;id&gt;.json —
/// replaces the Python version's full telemetry rescan on every edit event.</summary>
public sealed class SessionState
{
    [JsonPropertyName("edited_slices")] public List<string> EditedSlices { get; set; } = new();
}

/// <summary>All JSON reads go through source generation — no reflection, NativeAOT-safe.
/// (Writes deliberately do NOT use System.Text.Json: PyJson owns the byte format.)</summary>
[JsonSerializable(typeof(HookEvent))]
[JsonSerializable(typeof(MapModel))]
[JsonSerializable(typeof(TelemetryRecord))]
[JsonSerializable(typeof(SessionState))]
internal sealed partial class HeimdallJsonContext : JsonSerializerContext;
```

`src/Heimdall.Cli/FileSessionStore.cs`:

```csharp
using System.Text;
using System.Text.Json;
using Heimdall.Sensors;

namespace Heimdall.Cli;

/// <summary>
/// O(1)-per-event session state: which slices this session has already edited.
/// The Python version answered that by rescanning all of telemetry.jsonl on every
/// edit (O(n²) per session); this store is the fix the port was asked to make.
/// </summary>
internal sealed class FileSessionStore(string heimdallDir) : ISessionStore
{
    private string PathFor(string session)
    {
        var safe = new StringBuilder(session.Length);
        foreach (var c in session)
            safe.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_');
        return Path.Combine(heimdallDir, $"session-{safe}.json");
    }

    public IReadOnlyCollection<string> GetEditedSlices(string session)
    {
        var p = PathFor(session);
        if (!File.Exists(p)) return Array.Empty<string>();
        try
        {
            var state = JsonSerializer.Deserialize(File.ReadAllText(p), HeimdallJsonContext.Default.SessionState);
            return state?.EditedSlices ?? (IReadOnlyCollection<string>)Array.Empty<string>();
        }
        catch (JsonException) { return Array.Empty<string>(); }
    }

    public void AddEditedSlice(string session, string slice)
    {
        var slices = new SortedSet<string>(GetEditedSlices(session), StringComparer.Ordinal);
        if (!slices.Add(slice)) return;
        Directory.CreateDirectory(heimdallDir);
        var json = JsonSerializer.Serialize(new SessionState { EditedSlices = slices.ToList() },
            HeimdallJsonContext.Default.SessionState);
        File.WriteAllText(PathFor(session), json, new UTF8Encoding(false));
    }
}
```

`src/Heimdall.Cli/HookCommand.cs`:

```csharp
using System.Text;
using System.Text.Json;
using Heimdall.Sensors;

namespace Heimdall.Cli;

/// <summary>
/// `heimdall hook` — the PostToolUse sensor runner (port of heimdall.py).
/// stdin: one hook event JSON. Runs every registered sensor, appends findings to
/// .heimdall/telemetry.jsonl (Python-json byte format), and speaks feedback to the
/// agent via stderr + exit 2. Anything unparseable on stdin exits 0: a broken hook
/// must never block the agent's tool call.
/// </summary>
internal static class HookCommand
{
    public static int Run(TextReader stdin, TextWriter stderr, string root)
    {
        HookEvent? ev;
        try { ev = JsonSerializer.Deserialize(stdin.ReadToEnd(), HeimdallJsonContext.Default.HookEvent); }
        catch (JsonException) { return 0; }
        if (ev is null) return 0;

        var heimdallDir = Path.Combine(root, ".heimdall");
        MapModel? map = null;
        var mapPath = Path.Combine(heimdallDir, "map.json");
        if (File.Exists(mapPath))
        {
            try { map = JsonSerializer.Deserialize(File.ReadAllText(mapPath), HeimdallJsonContext.Default.MapModel); }
            catch (JsonException e) { stderr.WriteLine($"heimdall: unreadable .heimdall/map.json: {e.Message}"); return 1; }
        }

        var ctx = new HeimdallContext { Map = map, Sessions = new FileSessionStore(heimdallDir) };
        var findings = new List<Finding>();
        var feedback = new List<string>();
        foreach (var sensor in SensorRegistry.All)
        {
            try
            {
                foreach (var f in sensor.Observe(ev, ctx))
                {
                    f.Sensor ??= sensor.Name;
                    f.Ts = UnixNow();
                    f.Session = ev.SessionOrQ;
                    findings.Add(f);
                    if (!string.IsNullOrEmpty(f.Feedback)) feedback.Add(f.Feedback);
                }
            }
            catch (Exception e)
            {
                findings.Add(new Finding { Sensor = "heimdall", Error = e.Message, Ts = UnixNow() });
            }
        }

        if (findings.Count > 0)
        {
            Directory.CreateDirectory(heimdallDir);
            var sb = new StringBuilder();
            foreach (var f in findings) sb.Append(PyJson.Line(f)).Append('\n');
            File.AppendAllText(Path.Combine(heimdallDir, "telemetry.jsonl"), sb.ToString(), new UTF8Encoding(false));
        }

        if (feedback.Count > 0)
        {
            stderr.WriteLine("Heimdall: " + string.Join(" | ", feedback));
            return 2; // exit 2 => Claude Code surfaces stderr to the agent
        }
        return 0;
    }

    private static double UnixNow() => (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;
}
```

In `HeimdallApp.cs`, replace the stub case with:

```csharp
            case "hook": return HookCommand.Run(stdin, stderr, root);
            case "drift":
                stderr.WriteLine("heimdall drift: not yet ported"); return 1;
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Heimdall.Tests -c Release`
Expected: PASS (all).

- [ ] **Step 5: Telemetry byte-parity against the Python runner (Git Bash, repo root)**

```bash
cd /c/Users/MCSN/RiderProjects/brokkr-eitri
dotnet publish src/Heimdall.Cli -c Release -r win-x64
BIN=src/Heimdall.Cli/bin/Release/net10.0/win-x64/publish/heimdall.exe
ev() { printf '{"session_id":"%s","tool_name":"%s","tool_input":{"file_path":"%s"}}' "$1" "$2" "$3"; }
run_seq() {  # $1 = hook command
  rm -rf .heimdall && git checkout -- samples
  $BIN map --root samples --budget 15000 >/dev/null  # same map for both runs
  ev s1 Read  samples/Slices/Domme/Internal/DommeEngine.cs             | $1 || true
  ev s1 Edit  samples/Slices/Domme/Internal/DommeEngine.cs             | $1 || true
  ev s1 Read  samples/Slices/Retskilder/Contract/IRetskilderService.cs | $1 || true
  ev s1 Edit  samples/Slices/Retskilder/Internal/RetskilderEngine.cs   | $1 || true
  ev s2 Edit  samples/Slices/Domme/Internal/DommeService.cs            | $1 || true
  ev s2 Read  samples/Slices/Retskilder/Internal/RetskilderEngine.cs   | $1 || true
  sed -E 's/"ts": [0-9.e+]+/"ts": T/' .heimdall/telemetry.jsonl
}
run_seq "py -3 heimdall/heimdall.py" | tr -d '\r' > /tmp/py-tele.jsonl
run_seq "$BIN hook"                  | tr -d '\r' > /tmp/net-tele.jsonl
diff /tmp/py-tele.jsonl /tmp/net-tele.jsonl && echo TELEMETRY-PARITY-OK
git checkout -- samples && rm -rf .heimdall
```

Expected: `TELEMETRY-PARITY-OK` (ts normalized to `T`; CR stripped because Python text mode writes CRLF on Windows).
Note: the Python runner resolves the repo root from its own file location, so this must run from the repo root — which is also why parity holds. The Python map.json is fine as input for the Python run because Task 2 proved map parity; using the CLI's map for both keeps the sequence identical.

- [ ] **Step 6: Commit**

```bash
git add src/Heimdall.Cli tests/Heimdall.Tests
git commit -m "Heimdall.Cli: hook subcommand — sensor runner with O(1) per-session state"
```
(body: port of heimdall.py; byte-parity verified; O(n^2) telemetry rescan replaced by .heimdall/session-<id>.json via FileSessionStore; Co-Authored-By trailer)

---

### Task 4: `drift` subcommand (port of drift_report.py)

**Files:**
- Create: `src/Heimdall.Cli/DriftCommand.cs`
- Modify: `src/Heimdall.Cli/HeimdallApp.cs` (replace the `drift` case)
- Test: `tests/Heimdall.Tests/DriftCommandTests.cs`

**Interfaces:**
- Consumes: `HeimdallJsonContext.Default.MapModel/.TelemetryRecord`.
- Produces: `DriftCommand.Run(TextWriter stdout, TextWriter stderr, string root)` → int.

**Faithful-port notes (from drift_report.py, verified against source — column semantics must be exact):**
- missing telemetry or map → stderr `need telemetry + map`, exit 1.
- telemetry lines that fail to parse are skipped; session key = `session` field or `?`; `event=="edit"` collects `slice` into a set, `event=="read"` collects `kind`.
- session **insertion order = first appearance in telemetry** (Python dict); per-slice Counter insertion order = first time a target gets a read attributed.
- per-session table FIRST (wandering is a session property; aggregates dilute it): header `{'session':<10}{'slices edited':<26}{'reads':>7}{'oob':>6}{'oob %':>8}`, rows sorted by session id (ordinal), only sessions with ≥1 read, `slices edited` = comma-joined sorted edits or `-`.
- a read is in-bounds iff kind == `kernel`, or `slice:X` with X in the session's edited set, or `contract:X` with X in (edits ∪ depends_on of each edited slice).
- per-slice aggregate SECOND: target = first sorted edited slice, or `?` if the session edited nothing; header `{'slice':<22}{'reads':>7}{'out-of-bounds':>15}{'oob %':>8}`; rows sorted by descending oob count, stable (ties keep first-seen order).
- percentages are Python `.0f` = round-half-even → `Math.Round(v)` then `PadLeft(7)` + `"%"`.
- blank line between tables; footer `\nrule of thumb: sustained oob% > 20 on a slice = re-cut that seam`.

- [ ] **Step 1: Write the failing tests**

`tests/Heimdall.Tests/DriftCommandTests.cs`:

```csharp
using Xunit;

namespace Heimdall.Tests;

public class DriftCommandTests
{
    private static TempRepo SmokeScenario()
    {
        var repo = new TempRepo();
        repo.WriteSampleMap();
        // session s1: edits Domme, reads own internal + declared contract; then creeps into Retskilder
        repo.Hook(TempRepo.Ev("s1", "Read", "samples/Slices/Domme/Internal/DommeEngine.cs"));
        repo.Hook(TempRepo.Ev("s1", "Edit", "samples/Slices/Domme/Internal/DommeEngine.cs"));
        repo.Hook(TempRepo.Ev("s1", "Read", "samples/Slices/Retskilder/Contract/IRetskilderService.cs"));
        repo.Hook(TempRepo.Ev("s1", "Read", "samples/Slices/Retskilder/Internal/RetskilderEngine.cs")); // OOB
        repo.Hook(TempRepo.Ev("s1", "Edit", "samples/Slices/Retskilder/Internal/RetskilderEngine.cs"));
        // session s2: edits Domme only, one clean read + one OOB read
        repo.Hook(TempRepo.Ev("s2", "Edit", "samples/Slices/Domme/Internal/DommeService.cs"));
        repo.Hook(TempRepo.Ev("s2", "Read", "samples/Slices/Domme/Internal/DommeEngine.cs"));
        repo.Hook(TempRepo.Ev("s2", "Read", "samples/Slices/Retskilder/Internal/RetskilderEngine.cs")); // OOB
        return repo;
    }

    [Fact]
    public void Drift_PrintsPerSessionTableFirst_ExactColumns()
    {
        using var repo = SmokeScenario();
        var r = repo.Run("", "drift");
        Assert.Equal(0, r.Code);
        var lines = r.Stdout.Replace("\r\n", "\n").Split('\n');
        Assert.Equal("session   slices edited               reads   oob   oob %", lines[0]);
        // s1 edited both slices; 3 reads; the Retskilder Internal read happened BEFORE the
        // s1 Retskilder edit, but session sets are computed over the whole session -> in-bounds
        Assert.Equal("s1        Domme,Retskilder                3     0      0%", lines[1]);
        // s2: 2 reads, 1 OOB -> 50%
        Assert.Equal("s2        Domme                           2     1     50%", lines[2]);
    }

    [Fact]
    public void Drift_PerSliceAggregateSecond_AttributesToFirstEditedSlice()
    {
        using var repo = SmokeScenario();
        var r = repo.Run("", "drift");
        var text = r.Stdout.Replace("\r\n", "\n");
        var lines = text.Split('\n');
        Assert.Equal("", lines[3]);
        Assert.Equal("slice                   reads  out-of-bounds   oob %", lines[4]);
        // Domme first (1 oob) then nothing else in this scenario: s1's target is "Domme"
        // (first sorted edit) with 0/3, s2's target "Domme" adds 1/2 -> Domme 5 reads 1 oob 20%
        Assert.Equal("Domme                       5              1     20%", lines[5]);
        Assert.Contains("rule of thumb: sustained oob% > 20", text);
    }

    [Fact]
    public void Drift_MissingInputs_Exit1()
    {
        using var repo = new TempRepo();
        var r = repo.Run("", "drift");
        Assert.Equal(1, r.Code);
        Assert.Contains("need telemetry + map", r.Stderr);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Heimdall.Tests -c Release --filter DriftCommandTests`
Expected: FAIL — `heimdall drift: not yet ported` (the MissingInputs test may pass by accident of exit code 1; the table tests must fail).

- [ ] **Step 3: Implement**

`src/Heimdall.Cli/DriftCommand.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using Heimdall.Sensors;

namespace Heimdall.Cli;

/// <summary>
/// `heimdall drift` — actual agent behavior (telemetry) vs the architecture's promise
/// (map). Port of drift_report.py: per-session table first (wandering is a session
/// property; aggregates dilute it), then the per-slice aggregate. Column semantics
/// and formatting are byte-identical to the Python report.
/// </summary>
internal static class DriftCommand
{
    private sealed class Session
    {
        public readonly HashSet<string> Edits = new(StringComparer.Ordinal);
        public readonly List<string> ReadKinds = new();
    }

    public static int Run(TextWriter stdout, TextWriter stderr, string root)
    {
        var teleP = Path.Combine(root, ".heimdall", "telemetry.jsonl");
        var mapP = Path.Combine(root, ".heimdall", "map.json");
        if (!(File.Exists(teleP) && File.Exists(mapP))) { stderr.WriteLine("need telemetry + map"); return 1; }
        var m = JsonSerializer.Deserialize(File.ReadAllText(mapP), HeimdallJsonContext.Default.MapModel)!;

        // insertion-ordered sessions, exactly like a Python dict
        var sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        var sessionOrder = new List<string>();
        foreach (var line in File.ReadLines(teleP))
        {
            TelemetryRecord? f;
            try { f = JsonSerializer.Deserialize(line, HeimdallJsonContext.Default.TelemetryRecord); }
            catch (JsonException) { continue; }
            if (f is null) continue;
            var sid = f.Session ?? "?";
            if (!sessions.TryGetValue(sid, out var s)) { s = new Session(); sessions[sid] = s; sessionOrder.Add(sid); }
            if (f.Event == "edit" && f.Slice is not null) s.Edits.Add(f.Slice);
            else if (f.Event == "read") s.ReadKinds.Add(f.Kind ?? "");
        }

        HashSet<string> AllowedFor(Session s)
        {
            var allowed = new HashSet<string>(s.Edits, StringComparer.Ordinal);
            foreach (var e in s.Edits)
                if (m.Slices.TryGetValue(e, out var info))
                    allowed.UnionWith(info.DependsOn);
            return allowed;
        }

        static bool InBounds(string kind, Session s, HashSet<string> allowed) =>
            kind == "kernel"
            || (kind.StartsWith("slice:", StringComparison.Ordinal) && s.Edits.Contains(kind["slice:".Length..]))
            || (kind.StartsWith("contract:", StringComparison.Ordinal) && allowed.Contains(kind["contract:".Length..]));

        // per-slice aggregate, Counter-style insertion order
        var totalBySlice = new Dictionary<string, int>(StringComparer.Ordinal);
        var oobBySlice = new Dictionary<string, int>(StringComparer.Ordinal);
        var sliceOrder = new List<string>();
        foreach (var sid in sessionOrder)
        {
            var s = sessions[sid];
            var allowed = AllowedFor(s);
            var target = s.Edits.Count > 0 ? s.Edits.Order(StringComparer.Ordinal).First() : "?";
            foreach (var kind in s.ReadKinds)
            {
                if (!totalBySlice.ContainsKey(target)) { totalBySlice[target] = 0; oobBySlice[target] = 0; sliceOrder.Add(target); }
                totalBySlice[target]++;
                if (!InBounds(kind, s, allowed)) oobBySlice[target]++;
            }
        }

        // per-session first (wandering is a session property; aggregates dilute it)
        stdout.WriteLine("session".PadRight(10) + "slices edited".PadRight(26) + "reads".PadLeft(7) + "oob".PadLeft(6) + "oob %".PadLeft(8));
        foreach (var sid in sessionOrder.Order(StringComparer.Ordinal))
        {
            var s = sessions[sid];
            var allowed = AllowedFor(s);
            var t = s.ReadKinds.Count;
            if (t == 0) continue;
            var o = s.ReadKinds.Count(k => !InBounds(k, s, allowed));
            var edited = s.Edits.Count > 0 ? string.Join(",", s.Edits.Order(StringComparer.Ordinal)) : "-";
            stdout.WriteLine(sid.PadRight(10) + edited.PadRight(26)
                + t.ToString(CultureInfo.InvariantCulture).PadLeft(7)
                + o.ToString(CultureInfo.InvariantCulture).PadLeft(6)
                + Pct(o, t).PadLeft(7) + "%");
        }
        stdout.WriteLine();
        stdout.WriteLine("slice".PadRight(22) + "reads".PadLeft(7) + "out-of-bounds".PadLeft(15) + "oob %".PadLeft(8));
        foreach (var sl in sliceOrder.OrderByDescending(x => oobBySlice[x]))  // stable: ties keep first-seen order
        {
            stdout.WriteLine(sl.PadRight(22)
                + totalBySlice[sl].ToString(CultureInfo.InvariantCulture).PadLeft(7)
                + oobBySlice[sl].ToString(CultureInfo.InvariantCulture).PadLeft(15)
                + Pct(oobBySlice[sl], totalBySlice[sl]).PadLeft(7) + "%");
        }
        stdout.WriteLine();
        stdout.WriteLine("rule of thumb: sustained oob% > 20 on a slice = re-cut that seam");
        return 0;
    }

    /// <summary>Python f"{v:.0f}" — round half to even, no decimals.</summary>
    private static string Pct(int oob, int total) =>
        Math.Round(100.0 * oob / total, MidpointRounding.ToEven).ToString("0", CultureInfo.InvariantCulture);
}
```

In `HeimdallApp.cs`, replace the stub case with:

```csharp
            case "drift": return DriftCommand.Run(stdout, stderr, root);
```

(The Python footer is `print("\nrule of thumb: ...")` — one leading newline. `stdout.WriteLine()` after the table + `WriteLine(footer)` reproduces it, because Python's table `print()` already ended the previous line.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Heimdall.Tests -c Release`
Expected: PASS (all).

- [ ] **Step 5: Drift byte-parity against Python (Git Bash, repo root)**

Reuse the Task 3 `run_seq` sequence to build telemetry with the .NET hook, then:

```bash
py -3 heimdall/drift_report.py | tr -d '\r' > /tmp/py-drift.txt
$BIN drift                     | tr -d '\r' > /tmp/net-drift.txt
diff /tmp/py-drift.txt /tmp/net-drift.txt && echo DRIFT-PARITY-OK
git checkout -- samples && rm -rf .heimdall
```

Expected: `DRIFT-PARITY-OK` — both reports read the same telemetry file, so this checks pure formatting.

- [ ] **Step 6: Commit**

```bash
git add src/Heimdall.Cli tests/Heimdall.Tests
git commit -m "Heimdall.Cli: drift subcommand — per-session table + per-slice aggregate, byte-parity with drift_report.py"
```
(with the Co-Authored-By trailer)

---

### Task 5: Behavioral suite — the 16 harness scenarios

**Files:**
- Test: `tests/Heimdall.Tests/BehavioralScenarios.cs`

**Interfaces:**
- Consumes: `TempRepo` (incl. `WriteSampleMap(withFrozenCore: true)`), `HeimdallApp.Run` via `repo.Hook`/`repo.Run`.
- Produces: nothing new — this is the acceptance suite mirroring the slicespike `harness_test.py` checks (criterion 2: 16/16).

- [ ] **Step 1: Write all 16 scenarios**

`tests/Heimdall.Tests/BehavioralScenarios.cs`:

```csharp
using Xunit;

namespace Heimdall.Tests;

/// <summary>
/// The 16 behavioral checks the Python harness was validated with (harness_test.py in the
/// slicespike experiments): feedback fires precisely on scope creep and frozen contracts,
/// stays silent on clean work, and the drift report shows wandering sessions undiluted.
/// </summary>
public class BehavioralScenarios
{
    // 1
    [Fact]
    public void CleanSession_EditsAndReadsOwnSlice_AllHooksSilent()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap();
        Assert.Equal((0, ""), repo.Hook(TempRepo.Ev("s1", "Edit", "samples/Slices/Domme/Internal/DommeEngine.cs")));
        Assert.Equal((0, ""), repo.Hook(TempRepo.Ev("s1", "Read", "samples/Slices/Domme/Internal/DommeService.cs")));
        Assert.Equal((0, ""), repo.Hook(TempRepo.Ev("s1", "Read", "samples/SharedKernel/Primitives.cs")));
        Assert.Equal((0, ""), repo.Hook(TempRepo.Ev("s1", "Read", "samples/Slices/Retskilder/Contract/IRetskilderService.cs")));
    }

    // 2
    [Fact]
    public void CleanSession_TelemetryStillLogged()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap();
        repo.Hook(TempRepo.Ev("s1", "Edit", "samples/Slices/Domme/Internal/DommeEngine.cs"));
        repo.Hook(TempRepo.Ev("s1", "Read", "samples/Slices/Domme/Internal/DommeService.cs"));
        var lines = repo.Telemetry.TrimEnd('\n').Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"event\": \"edit\"", lines[0]);
        Assert.Contains("\"event\": \"read\"", lines[1]);
    }

    // 3
    [Fact]
    public void Wanderer_ForeignInternalRead_LoggedNotWarned()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap();
        var (code, stderr) = repo.Hook(TempRepo.Ev("s1", "Read", "samples/Slices/Retskilder/Internal/RetskilderEngine.cs"));
        Assert.Equal(0, code);           // reads NEVER interrupt — pure observation
        Assert.Equal("", stderr);
        Assert.Contains("\"kind\": \"slice:Retskilder\"", repo.Telemetry);
    }

    // 4
    [Fact]
    public void ScopeCreep_SecondSliceEdit_WarnsWithBothSliceNames()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap();
        repo.Hook(TempRepo.Ev("s1", "Edit", "samples/Slices/Domme/Internal/DommeEngine.cs"));
        var (code, stderr) = repo.Hook(TempRepo.Ev("s1", "Edit", "samples/Slices/Retskilder/Internal/RetskilderEngine.cs"));
        Assert.Equal(2, code);
        Assert.Contains("you are now editing slice 'Retskilder' after editing ['Domme']", stderr);
        Assert.Contains("cross-slice changes should go through contracts", stderr);
    }

    // 5
    [Fact]
    public void ScopeCreep_WarnsExactlyOnce_ThirdEditSameSliceSilent()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap();
        repo.Hook(TempRepo.Ev("s1", "Edit", "samples/Slices/Domme/Internal/DommeEngine.cs"));
        Assert.Equal(2, repo.Hook(TempRepo.Ev("s1", "Edit", "samples/Slices/Retskilder/Internal/RetskilderEngine.cs")).Code);
        // the slice is now part of the session's working set — repeating the edit is not a new crossing
        Assert.Equal((0, ""), repo.Hook(TempRepo.Ev("s1", "Edit", "samples/Slices/Retskilder/Internal/RetskilderService.cs")));
    }

    // 6
    [Fact]
    public void ScopeCreep_SeparateSessions_NoWarning()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap();
        repo.Hook(TempRepo.Ev("s1", "Edit", "samples/Slices/Domme/Internal/DommeEngine.cs"));
        var (code, stderr) = repo.Hook(TempRepo.Ev("s2", "Edit", "samples/Slices/Retskilder/Internal/RetskilderEngine.cs"));
        Assert.Equal(0, code);
        Assert.Equal("", stderr);
    }

    // 7
    [Fact]
    public void FrozenContract_FanInAtThreshold_Warns()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap(withFrozenCore: true); // Core has fan_in 12 >= 10
        var (code, stderr) = repo.Hook(TempRepo.Ev("s1", "Edit", "samples/Slices/Core/Contract/ICoreService.cs"));
        Assert.Equal(2, code);
        Assert.Contains("'Core' Contract has fan-in 12 — treat as frozen", stderr);
        Assert.Contains("expand-contract", stderr);
    }

    // 8
    [Fact]
    public void LowFanIn_ContractEdit_Silent()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap(); // Retskilder fan_in 1 < 10
        Assert.Equal((0, ""), repo.Hook(TempRepo.Ev("s1", "Edit", "samples/Slices/Retskilder/Contract/IRetskilderService.cs")));
    }

    // 9
    [Fact]
    public void Read_KernelFile_ClassifiedKernel()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap();
        repo.Hook(TempRepo.Ev("s1", "Read", "samples/SharedKernel/Primitives.cs"));
        Assert.Contains("\"kind\": \"kernel\"", repo.Telemetry);
    }

    // 10
    [Fact]
    public void Read_ContractFile_ClassifiedContractWithSlice()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap();
        repo.Hook(TempRepo.Ev("s1", "Read", "samples/Slices/Retskilder/Contract/IRetskilderService.cs"));
        Assert.Contains("\"kind\": \"contract:Retskilder\"", repo.Telemetry);
    }

    // 11
    [Fact]
    public void Read_OutsideSlicesDir_ClassifiedOutside()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap();
        repo.Hook(TempRepo.Ev("s1", "Read", "docs/rules/EIT001.md"));
        Assert.Contains("\"kind\": \"outside\"", repo.Telemetry);
    }

    // 12
    [Fact]
    public void Read_NonCodeFile_ProducesNoFinding()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap();
        repo.Hook(TempRepo.Ev("s1", "Read", "samples/Slices/Domme/appsettings.json"));
        Assert.Equal("", repo.Telemetry);
    }

    // 13
    [Fact]
    public void Hook_IrrelevantTool_NoFindingNoTelemetry()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap();
        Assert.Equal((0, ""), repo.Hook("{\"session_id\":\"s1\",\"tool_name\":\"Bash\",\"tool_input\":{\"command\":\"ls\"}}"));
        Assert.Equal("", repo.Telemetry);
    }

    // 14
    [Fact]
    public void Hook_GarbageStdin_Exit0()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap();
        var r = repo.Run("{not json", "hook");
        Assert.Equal(0, r.Code);
        Assert.Equal("", r.Stderr);
    }

    // 15
    [Fact]
    public void Hook_NoMap_StaysSilentEvenOnCrossSliceEdits()
    {
        using var repo = new TempRepo();
        repo.Hook(TempRepo.Ev("s1", "Edit", "samples/Slices/Domme/Internal/DommeEngine.cs"));
        var (code, stderr) = repo.Hook(TempRepo.Ev("s1", "Edit", "samples/Slices/Retskilder/Internal/RetskilderEngine.cs"));
        Assert.Equal(0, code);
        Assert.Equal("", stderr);
        Assert.Equal("", repo.Telemetry);
    }

    // 16
    [Fact]
    public void Drift_PerSession_WanderingSessionShows100PercentUndiluted()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap();
        // clean session: 3 reads, all in bounds
        repo.Hook(TempRepo.Ev("clean", "Edit", "samples/Slices/Domme/Internal/DommeEngine.cs"));
        repo.Hook(TempRepo.Ev("clean", "Read", "samples/Slices/Domme/Internal/DommeService.cs"));
        repo.Hook(TempRepo.Ev("clean", "Read", "samples/SharedKernel/Primitives.cs"));
        repo.Hook(TempRepo.Ev("clean", "Read", "samples/Slices/Retskilder/Contract/IRetskilderService.cs"));
        // wandering session: edits Domme, then reads ONLY foreign internals
        repo.Hook(TempRepo.Ev("wander", "Edit", "samples/Slices/Domme/Internal/DommeEngine.cs"));
        repo.Hook(TempRepo.Ev("wander", "Read", "samples/Slices/Retskilder/Internal/RetskilderEngine.cs"));
        repo.Hook(TempRepo.Ev("wander", "Read", "samples/Slices/Retskilder/Internal/RetskilderService.cs"));

        var stdout = repo.Run("", "drift").Stdout.Replace("\r\n", "\n");
        var lines = stdout.Split('\n');
        // per-session table: the wanderer shows 100%, NOT averaged with the clean session
        Assert.Equal("clean     Domme                           3     0      0%", lines[1]);
        Assert.Equal("wander    Domme                           2     2    100%", lines[2]);
        // the aggregate DOES dilute (5 reads, 2 oob -> 40%) — which is exactly why
        // the per-session table exists and prints first
        Assert.Contains("Domme                       5              2     40%", stdout);
    }
}
```

- [ ] **Step 2: Run the full suite**

Run: `dotnet test tests/Heimdall.Tests -c Release`
Expected: PASS — 16 BehavioralScenarios + the format/command tests from Tasks 1–4. If scenario 5 fails on the third edit: check `FileSessionStore.AddEditedSlice` recorded Retskilder on the warned edit (it must — Python's telemetry-scan equivalent recorded it too, because the finding was logged).

- [ ] **Step 3: Commit**

```bash
git add tests/Heimdall.Tests
git commit -m "Heimdall.Tests: the 16 behavioral harness scenarios, ported from the slicespike suite"
```
(with the Co-Authored-By trailer)

---

### Task 6: Smoke test rewrite, latency gate, Python deletion, docs + settings

**Files:**
- Modify: `heimdall/smoke_test.sh` (full rewrite below)
- Delete: `heimdall/heimdall.py`, `heimdall/emit_map.py`, `heimdall/drift_report.py`, `heimdall/sensors/` (whole dir incl. `__pycache__`), `tools/calibrate.py`
- Create: `docs/calibration.md`
- Modify: `.claude/settings.json`, `CONTRIBUTING.md` (lines 9–10), `README.md` (add build/install lines to the Heimdall section)

**Interfaces:**
- Consumes: the published AOT binary (all four subcommands).
- Produces: the CI-facing smoke contract — `bash heimdall/smoke_test.sh` green means map/hook/drift/estimate work end-to-end and hook latency ≤ 30 ms/event (printed).

**Note on the exact-string drift assertions:** before deleting the Python files, the final parity run (Step 2) is the authority. If any hardcoded table row in `DriftCommandTests`/`BehavioralScenarios` disagrees with the actual `py -3 heimdall/drift_report.py` output on identical telemetry, fix the C# formatting (not the Python) and update the test literal.

- [ ] **Step 1: Rewrite `heimdall/smoke_test.sh`**

```bash
#!/usr/bin/env bash
# End-to-end harness test: feedforward -> sense -> feedback -> drift.
# Drives the published NativeAOT binary: set HEIMDALL_BIN, or let it auto-detect.
set -eu; cd "$(dirname "$0")/.."

if [ -z "${HEIMDALL_BIN:-}" ]; then
  for rid in linux-x64 win-x64 osx-arm64 osx-x64; do
    for exe in heimdall heimdall.exe; do
      cand="src/Heimdall.Cli/bin/Release/net10.0/$rid/publish/$exe"
      if [ -f "$cand" ]; then HEIMDALL_BIN="$cand"; break 2; fi
    done
  done
fi
if [ -z "${HEIMDALL_BIN:-}" ]; then
  echo "no published heimdall binary — run: dotnet publish src/Heimdall.Cli -c Release -r <rid>" >&2
  exit 1
fi

rm -rf .heimdall
"$HEIMDALL_BIN" map --root samples --budget 15000 | grep -q "2 slices" && echo "ok: feedforward map emitted"
grep -q "heimdall:deps" samples/Slices/Domme/AGENTS.md && echo "ok: AGENTS.md deps generated from csproj"
ev() { printf '{"session_id":"s1","tool_name":"%s","tool_input":{"file_path":"%s"}}' "$1" "$2"; }
ev Read "samples/Slices/Domme/Internal/DommeEngine.cs"        | "$HEIMDALL_BIN" hook
ev Edit "samples/Slices/Domme/Internal/DommeEngine.cs"        | "$HEIMDALL_BIN" hook
ev Read "samples/Slices/Retskilder/Contract/IRetskilderService.cs" | "$HEIMDALL_BIN" hook
ev Read "samples/Slices/Retskilder/Internal/RetskilderEngine.cs"   | "$HEIMDALL_BIN" hook   # OOB read
out=$(ev Edit "samples/Slices/Retskilder/Internal/RetskilderEngine.cs" | "$HEIMDALL_BIN" hook 2>&1 >/dev/null) && rc=0 || rc=$?
[ "$rc" = 2 ] && echo "$out" | grep -q "cross-slice" && echo "ok: feedback fired on second-slice edit (exit 2 -> agent sees it)"
# clean session: edits Domme only, reads a foreign Internal -> must count as OOB
ev2() { printf '{"session_id":"s2","tool_name":"%s","tool_input":{"file_path":"%s"}}' "$1" "$2"; }
ev2 Edit "samples/Slices/Domme/Internal/DommeService.cs"          | "$HEIMDALL_BIN" hook
ev2 Read "samples/Slices/Domme/Internal/DommeEngine.cs"           | "$HEIMDALL_BIN" hook
ev2 Read "samples/Slices/Retskilder/Internal/RetskilderEngine.cs" | "$HEIMDALL_BIN" hook
"$HEIMDALL_BIN" drift | grep -E "Domme.*[1-9][0-9]*%" >/dev/null && echo "ok: OOB read detected and attributed"
lines=$(wc -l < .heimdall/telemetry.jsonl)
[ "$lines" -ge 5 ] && echo "ok: telemetry logged ($lines findings)"
"$HEIMDALL_BIN" drift | grep -q "Domme" && echo "ok: drift report attributes out-of-bounds reads"

# estimator: same linked TokenEstimator source Eitri compiles with (parity is structural)
est=$("$HEIMDALL_BIN" estimate samples)
[ "$est" -gt 0 ] && echo "ok: estimate runs on the samples tree ($est tokens)"

# hook latency: this runs as a PostToolUse hook on EVERY tool call — must stay cheap.
# (bash 5's EPOCHREALTIME; measures full process lifecycle incl. spawn, like Claude Code does)
n=50; start=$EPOCHREALTIME
for _ in $(seq $n); do ev Read "samples/Slices/Domme/Internal/DommeEngine.cs" | "$HEIMDALL_BIN" hook; done
end=$EPOCHREALTIME
ms=$(awk -v a="$start" -v b="$end" -v n="$n" 'BEGIN{printf "%.1f", (b-a)*1000/n}')
echo "hook latency: ${ms} ms/event (n=$n)"
awk -v ms="$ms" 'BEGIN{exit !(ms <= 30)}' && echo "ok: hook latency <= 30ms/event"
echo "--- heimdall smoke: all green"
```

- [ ] **Step 2: Final byte-parity run, THEN delete the Python files**

Run the Task 3 telemetry parity + Task 2 map/AGENTS parity + Task 4 drift parity blocks once more against the freshly published binary. All three `*-PARITY-OK` markers must print. Then:

```bash
git rm heimdall/heimdall.py heimdall/emit_map.py heimdall/drift_report.py
git rm -r --cached heimdall/sensors 2>/dev/null || true
rm -rf heimdall/sensors
git rm tools/calibrate.py
```

(If `heimdall/sensors/*.py` are tracked, `git rm -r heimdall/sensors` removes them; `__pycache__` is untracked — plain `rm -rf`.)

- [ ] **Step 3: Create `docs/calibration.md`**

```markdown
# Calibrating the token estimator

`src/Shared/TokenEstimator.cs` is the single source of truth for token counting —
source-linked into both Eitri.Analyzers (the EIT100/EIT101 compile-time gate) and the
`heimdall` CLI (`heimdall estimate`), so the harness and the compiler cannot drift.

To re-check its calibration against a real tokenizer, compare `heimdall estimate`
with tiktoken's `o200k_base` on any C# tree (tiktoken is a Python library — this doc
is the one intentional Python mention left in the repo):

    heimdall estimate samples        # estimator total for all .cs under samples/

    python3 -c "import sys,os,tiktoken; enc=tiktoken.get_encoding('o200k_base'); \
      print(sum(len(enc.encode(open(os.path.join(r,f),encoding='utf-8').read())) \
      for r,_,fs in os.walk(sys.argv[1]) for f in fs if f.endswith('.cs')))" samples

Target: within ~±10% of o200k_base, erring conservative (+) on typical C# — a stricter
budget is the safe failure mode. If you retune the estimator, update the README claim.
```

- [ ] **Step 4: Update `.claude/settings.json`, `CONTRIBUTING.md`, `README.md`**

`.claude/settings.json` (the published binary path; on this machine the RID is win-x64):

```json
{
  "hooks": {
    "PostToolUse": [
      {
        "matcher": "Read|Grep|Glob|Edit|Write|MultiEdit",
        "hooks": [{ "type": "command", "command": "src/Heimdall.Cli/bin/Release/net10.0/win-x64/publish/heimdall.exe hook" }]
      }
    ]
  }
}
```

`CONTRIBUTING.md` — replace lines 9–10 with:

```markdown
- New Heimdall sensors: implement `ISensor` in `src/Heimdall.Sensors/` and add one line to `SensorRegistry.cs` (explicit registration, no reflection scanning — that's what keeps the CLI NativeAOT-clean); cover it in `tests/Heimdall.Tests` and extend `heimdall/smoke_test.sh` with an event that exercises it.
- `dotnet test tests/Heimdall.Tests` and `bash heimdall/smoke_test.sh` (needs `dotnet publish src/Heimdall.Cli -c Release -r <rid>` first) must be green.
```

`README.md` — in the Heimdall section, after the sensor code block (line ~185), replace the `heimdall/smoke_test.sh runs the whole loop...` sentence with:

```markdown
**Install / build the CLI.** Either as a dotnet tool — `dotnet tool install --global Heimdall.Cli` — or from source: `dotnet publish src/Heimdall.Cli -c Release -r win-x64` (or `linux-x64`/`osx-arm64`) and point `.claude/settings.json`'s hook at `src/Heimdall.Cli/bin/Release/net10.0/<rid>/publish/heimdall(.exe) hook`. `heimdall/smoke_test.sh` runs the whole loop end-to-end against the published binary (in CI too, where it also prints measured hook latency).
```

And in the Run Brokkr block comment, keep `bash heimdall/smoke_test.sh` as-is (still valid).

- [ ] **Step 5: Full local verification**

```bash
dotnet publish src/Heimdall.Cli -c Release -r win-x64
dotnet test tests/Heimdall.Tests -c Release
bash heimdall/smoke_test.sh          # must print latency + all ok lines
git checkout -- samples && rm -rf .heimdall
grep -ril python --include='*' . --exclude-dir=.git --exclude-dir=bin --exclude-dir=obj
```

Expected: smoke all green with `hook latency: X ms/event` (X ≤ 30); the grep hits only `docs/calibration.md` (and this plan file under docs/superpowers/). Zero `.py` files: `git ls-files '*.py'` → empty.
If latency exceeds 30 ms only because of Git Bash pipe/spawn overhead on Windows, verify the binary alone with a pre-written event file (`"$HEIMDALL_BIN" hook < /tmp/ev.json` in the loop) — the CI linux number is the one criterion 3 prints.

- [ ] **Step 6: Commit (Python deletion + smoke rewrite together, as required)**

```bash
git add heimdall/smoke_test.sh docs/calibration.md .claude/settings.json CONTRIBUTING.md README.md
git add -u
git commit -m "Heimdall: retire the Python prototype — smoke test drives the NativeAOT binary

Byte-parity (map.json, AGENTS.md markers, telemetry modulo ts, drift report) verified
against the Python implementation on the smoke event sequence before deletion.
tools/calibrate.py's duplicated estimator is gone: heimdall estimate + docs/calibration.md
replace it. Smoke test now also gates hook latency at 30ms/event.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 7: CI — AOT builds for linux-x64 + win-x64

**Files:**
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: everything above; `smoke_test.sh` auto-detects the published binary per RID.
- Produces: the acceptance-criterion CI matrix (criterion 5). Brokkr + package test stay linux-only (they are bash scripts that were never Windows-tested; criterion 5 requires them to run, not to run everywhere).

- [ ] **Step 1: Replace `.github/workflows/ci.yml`**

```yaml
name: ci
on: [push, pull_request]
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.x' }
      - name: Build analyzer
        run: dotnet build src/Eitri.Analyzers -c Release
      - name: Test analyzer
        run: dotnet test tests/Eitri.Analyzers.Tests -c Release
      - name: Test Heimdall (16 behavioral scenarios + format parity)
        run: dotnet test tests/Heimdall.Tests -c Release
      - name: Publish Heimdall (NativeAOT linux-x64)
        run: dotnet publish src/Heimdall.Cli -c Release -r linux-x64
      - name: Heimdall harness smoke test (prints hook latency)
        run: bash heimdall/smoke_test.sh
      - name: Pack
        run: dotnet pack src/Eitri.Analyzers -c Release -o out
      - name: Canary — the walls must bite
        run: bash samples/brokkr.sh
      - name: Package consumption test
        run: bash samples/test-package.sh
      - uses: actions/upload-artifact@v4
        with: { name: nupkg, path: out/*.nupkg }
  heimdall-windows:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.x' }
      - name: Test Heimdall
        run: dotnet test tests/Heimdall.Tests -c Release
      - name: Publish Heimdall (NativeAOT win-x64)
        run: dotnet publish src/Heimdall.Cli -c Release -r win-x64
      - name: Heimdall harness smoke test (prints hook latency)
        run: bash heimdall/smoke_test.sh
        shell: bash
```

- [ ] **Step 2: Sanity-check the workflow locally where possible**

Run: `dotnet publish src/Heimdall.Cli -c Release -r win-x64 && bash heimdall/smoke_test.sh` once more from a clean tree (`git status` clean apart from expected `.heimdall/` which is gitignored; `git checkout -- samples` afterwards).
Expected: green. YAML lint by eye — indentation, step names.

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "CI: NativeAOT Heimdall on linux-x64 + win-x64, behavioral tests, smoke with latency gate

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Acceptance criteria → task map (self-review)

1. smoke rewritten to AOT binary, byte-identical telemetry/map, Python deleted same commit → Tasks 6 (parity in 2/3/4 first)
2. 16/16 behavioral scenarios in tests/Heimdall.Tests → Task 5
3. hook latency ≤ 30ms/event printed in CI → Task 6 (smoke) + Task 7 (CI runs smoke)
4. estimator parity (same linked source, exact match) → Task 1 (`Estimate_File_MatchesLinkedEstimator`) + smoke estimate check
5. CI linux-x64 + win-x64 AOT, tests, smoke, Brokkr, package test; README updated → Task 7 + Task 6 (README/CONTRIBUTING)
6. zero .py files; "python" grep only CI/docs-intentional → Task 6 (deletion + docs/calibration.md)

Known deliberate divergences from the Python (all documented in code comments): CLI resolves repo root from CWD (hooks run with CWD = project root); always-LF output files; invalid map.json → clean error instead of traceback; session state file instead of O(n²) telemetry rescan; `SliceOf` separator normalization (fixes a real Python cross-platform misclassification bug).
