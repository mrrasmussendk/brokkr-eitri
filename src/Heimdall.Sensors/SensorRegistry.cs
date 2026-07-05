namespace Heimdall.Sensors;

/// <summary>
/// The sensors Heimdall runs, in order. This is the whole discovery mechanism — an explicit
/// list, deliberately NOT reflection scanning, so the CLI publishes cleanly with NativeAOT.
///
/// Adding a sensor: implement <see cref="ISensor"/> and add one line here. (That's the trade
/// for AOT: a file drop plus one registry line instead of a pure file drop.)
///
/// Order mirrors the Python runner's sorted-filename order (boundary_edits before boundary_reads).
/// </summary>
public static class SensorRegistry
{
    public static readonly IReadOnlyList<ISensor> All = new ISensor[]
    {
        new BoundaryEditsSensor(),
        new BoundaryReadsSensor(),
    };
}
