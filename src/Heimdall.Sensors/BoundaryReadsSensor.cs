namespace Heimdall.Sensors;

/// <summary>
/// Classifies every file the agent reads: in-slice / contract / kernel / out-of-bounds.
/// Pure observation — logs, never interrupts. Drift analysis happens in <see cref="DriftReport"/>.
/// </summary>
public sealed class BoundaryReadsSensor : ISensor
{
    public string Name => "boundary_reads";

    private static readonly string[] ReadTools = { "Read", "Grep", "Glob" };

    public IEnumerable<Finding> Observe(HookEvent e, HeimdallContext ctx)
    {
        if (Array.IndexOf(ReadTools, e.ToolName) < 0) yield break;
        var m = ctx.Map;
        if (m is null) yield break;
        var path = e.ReadPath;
        if (!(path.EndsWith(".cs", StringComparison.Ordinal)
              || path.EndsWith(".csproj", StringComparison.Ordinal)
              || path.EndsWith(".md", StringComparison.Ordinal))) yield break;

        var s = PathUtil.SliceOf(path, m);
        var kind = path.Contains(m.Kernel, StringComparison.Ordinal)
            ? "kernel"
            : (s is not null ? "slice:" + s : "outside");
        if (s is not null && PathUtil.Norm(path).Contains("/Contract/", StringComparison.Ordinal))
            kind = "contract:" + s;

        yield return new Finding { Event = "read", Path = path, Kind = kind };
    }
}
