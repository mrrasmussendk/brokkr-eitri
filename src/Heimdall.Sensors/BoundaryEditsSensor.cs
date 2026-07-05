using System.Text;

namespace Heimdall.Sensors;

/// <summary>
/// Feedback sensor: warns the agent in-loop when it edits a second slice in one session
/// (the classic scope-creep smell) or edits a frozen high fan-in contract.
///
/// The "which slices has this session already edited" lookup is served by the session store
/// (a tiny per-session state file), NOT by rescanning the whole telemetry log on every edit
/// as the Python version did (O(n²) per session).
/// </summary>
public sealed class BoundaryEditsSensor : ISensor
{
    public const int FanInFreeze = 10;

    public string Name => "boundary_edits";

    private static readonly string[] EditTools = { "Edit", "Write", "MultiEdit" };

    public IEnumerable<Finding> Observe(HookEvent e, HeimdallContext ctx)
    {
        if (Array.IndexOf(EditTools, e.ToolName) < 0) return [];
        var m = ctx.Map;
        if (m is null) return [];
        var path = e.EditPath;
        var s = PathUtil.SliceOf(path, m);
        if (s is null) return [];

        var finding = new Finding { Event = "edit", Path = path, Slice = s };

        var prior = ctx.Sessions.GetEditedSlices(e.SessionOrQ);
        if (prior.Count > 0 && !prior.Contains(s))
            finding.Feedback =
                $"you are now editing slice '{s}' after editing {PyList(prior)} — " +
                "cross-slice changes should go through contracts; confirm this is intentional";

        m.Slices.TryGetValue(s, out var info);
        if (PathUtil.Norm(path).Contains("/Contract/", StringComparison.Ordinal)
            && (info?.FanIn ?? 0) >= FanInFreeze)
            finding.Feedback =
                $"'{s}' Contract has fan-in {info!.FanIn} — treat as frozen: " +
                "additive changes only, breaking changes need an expand-contract fan-out";

        ctx.Sessions.AddEditedSlice(e.SessionOrQ, s);
        return [finding];
    }

    /// <summary>Reproduce Python's <c>repr(sorted(prior))</c>, e.g. <c>['Domme', 'Retskilder']</c>.</summary>
    private static string PyList(IReadOnlyCollection<string> items)
    {
        var sorted = new List<string>(items);
        sorted.Sort(StringComparer.Ordinal);
        var sb = new StringBuilder("[");
        for (var i = 0; i < sorted.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append('\'').Append(sorted[i]).Append('\'');
        }
        return sb.Append(']').ToString();
    }
}
