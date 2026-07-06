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
