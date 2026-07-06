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
