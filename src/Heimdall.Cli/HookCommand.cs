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
