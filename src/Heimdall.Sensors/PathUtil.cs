namespace Heimdall.Sensors;

internal static class PathUtil
{
    /// <summary>Normalize path separators to forward slashes.</summary>
    public static string Norm(string s) => s.Replace('\\', '/');

    /// <summary>
    /// Which slice folder a path lives under, or null. Faithful to the Python <c>_slice_of</c>,
    /// but separator-normalized so classification works regardless of whether the map's
    /// slices_dir or the event path use '/' or '\\' (the Python version silently misclassified
    /// everything when the two disagreed — a cross-platform bug this port fixes).
    /// </summary>
    public static string? SliceOf(string path, MapModel map)
    {
        var sd = Norm(map.SlicesDir);
        if (sd.Length == 0) return null;
        var p = Norm(path);
        var idx = p.IndexOf(sd, StringComparison.Ordinal);
        if (idx < 0) return null;
        var rest = p.Substring(idx + sd.Length).TrimStart('/');
        var slash = rest.IndexOf('/');
        var seg = slash < 0 ? rest : rest.Substring(0, slash);
        return seg.Length == 0 ? null : seg;
    }
}
