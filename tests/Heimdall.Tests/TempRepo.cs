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

    /// <summary>The samples-shaped map: Kvad depends on Rune; optional frozen high fan-in slice.</summary>
    public void WriteSampleMap(bool withFrozenCore = false)
    {
        var core = withFrozenCore
            ? ",\n    \"Core\": {\n      \"path\": \"samples/Slices/Core\",\n      \"depends_on\": [],\n      \"budget\": 15000,\n      \"fan_in\": 12\n    }"
            : "";
        WriteFile(".heimdall/map.json",
            "{\n  \"kernel\": \"SharedKernel\",\n  \"slices_dir\": \"samples/Slices\",\n  \"slices\": {\n" +
            "    \"Kvad\": {\n      \"path\": \"samples/Slices/Kvad\",\n      \"depends_on\": [\n        \"Rune\"\n      ],\n      \"budget\": 15000,\n      \"fan_in\": 0\n    },\n" +
            "    \"Rune\": {\n      \"path\": \"samples/Slices/Rune\",\n      \"depends_on\": [],\n      \"budget\": 15000,\n      \"fan_in\": 1\n    }" +
            core + "\n  }\n}");
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, true); } catch { /* Windows file locks — temp dir, best effort */ }
    }
}
