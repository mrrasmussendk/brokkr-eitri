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
