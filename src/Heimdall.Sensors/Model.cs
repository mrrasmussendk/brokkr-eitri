using System.Text.Json.Serialization;

namespace Heimdall.Sensors;

/// <summary>A Claude Code PostToolUse hook event (the subset Heimdall reads).</summary>
public sealed class HookEvent
{
    [JsonPropertyName("session_id")] public string? SessionId { get; set; }
    [JsonPropertyName("tool_name")] public string? ToolName { get; set; }
    [JsonPropertyName("tool_input")] public ToolInput? ToolInput { get; set; }

    /// <summary>session_id or "?" — mirrors Python <c>event.get("session_id", "?")</c>.</summary>
    public string SessionOrQ => string.IsNullOrEmpty(SessionId) ? "?" : SessionId;

    /// <summary>file_path, then path, then "" — mirrors boundary_reads' path resolution.</summary>
    public string ReadPath => ToolInput?.FilePath ?? ToolInput?.Path ?? "";

    /// <summary>file_path only — mirrors boundary_edits (no <c>path</c> fallback).</summary>
    public string EditPath => ToolInput?.FilePath ?? "";
}

public sealed class ToolInput
{
    [JsonPropertyName("file_path")] public string? FilePath { get; set; }
    [JsonPropertyName("path")] public string? Path { get; set; }
}

/// <summary>The feedforward map (.heimdall/map.json): dependency graph + budgets + fan-in.</summary>
public sealed class MapModel
{
    [JsonPropertyName("kernel")] public string Kernel { get; set; } = "SharedKernel";
    [JsonPropertyName("slices_dir")] public string SlicesDir { get; set; } = "";
    [JsonPropertyName("slices")] public Dictionary<string, SliceInfo> Slices { get; set; } = new();
}

public sealed class SliceInfo
{
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("depends_on")] public List<string> DependsOn { get; set; } = new();
    [JsonPropertyName("budget")] public int Budget { get; set; }
    [JsonPropertyName("fan_in")] public int FanIn { get; set; }
}

/// <summary>One telemetry line, as read back by the drift report (extra keys ignored).</summary>
public sealed class TelemetryRecord
{
    [JsonPropertyName("event")] public string? Event { get; set; }
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    [JsonPropertyName("slice")] public string? Slice { get; set; }
    [JsonPropertyName("session")] public string? Session { get; set; }
}

/// <summary>
/// A sensor finding. Field order here is the byte-compatible emission order that mirrors
/// the Python dict insertion order across all producers:
/// event, path, kind, slice, feedback, sensor, error, ts, session. Null fields are omitted.
/// Serialized by PyJson (not System.Text.Json) to match Python's json.dumps exactly.
/// </summary>
public sealed class Finding
{
    public string? Event;
    public string? Path;
    public string? Kind;
    public string? Slice;
    public string? Feedback;
    public string? Sensor;
    public string? Error;
    public double Ts;
    public string? Session;
}

/// <summary>Per-session record of which slices an agent has already edited this session.</summary>
public interface ISessionStore
{
    IReadOnlyCollection<string> GetEditedSlices(string session);
    void AddEditedSlice(string session, string slice);
}

/// <summary>What a sensor sees: the parsed map (or null) and per-session edit history.</summary>
public sealed class HeimdallContext
{
    public required MapModel? Map { get; init; }
    public required ISessionStore Sessions { get; init; }
}

/// <summary>
/// A Heimdall sensor. Adding one = implement this and add a line to <see cref="SensorRegistry"/>.
/// Discovery is an explicit list (no reflection scanning) so the CLI stays NativeAOT-safe.
/// </summary>
public interface ISensor
{
    string Name { get; }
    IEnumerable<Finding> Observe(HookEvent e, HeimdallContext ctx);
}
