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
