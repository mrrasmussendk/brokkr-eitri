using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Heimdall.Cli;

/// <summary>
/// `heimdall map --root <dir> [--budget 15000] [--kernel SharedKernel]` — the feedforward
/// emitter (port of emit_map.py). The csproj graph is the ground truth: writes
/// .heimdall/map.json (relative to CWD) and regenerates the AGENTS.md deps markers.
/// </summary>
internal static partial class MapCommand
{
    [GeneratedRegex(@"\.\.\\(\w+)\\\w+\.csproj")]
    private static partial Regex RefRegex();

    [GeneratedRegex("<!--heimdall:deps-->.*?<!--/heimdall:deps-->", RegexOptions.Singleline)]
    private static partial Regex DepsRegex();

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, string cwd)
    {
        string? root = null;
        var budget = 15000;
        var kernel = "SharedKernel";
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--root" when i + 1 < args.Length: root = args[++i]; break;
                case "--budget" when i + 1 < args.Length: budget = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--kernel" when i + 1 < args.Length: kernel = args[++i]; break;
                default:
                    stderr.WriteLine("usage: heimdall map --root <dir> [--budget 15000] [--kernel SharedKernel]");
                    return 2;
            }
        }
        if (root is null)
        {
            stderr.WriteLine("usage: heimdall map --root <dir> [--budget 15000] [--kernel SharedKernel]");
            return 2;
        }

        var rootAbs = Path.GetFullPath(root, cwd);
        string? slicesDir = null;
        foreach (var cand in new[] { Path.Combine(rootAbs, "Slices"), Path.Combine(rootAbs, "src", "Slices") })
            if (Directory.Exists(cand)) { slicesDir = cand; break; }
        if (slicesDir is null) { stderr.WriteLine($"no Slices/ under {root}"); return 1; }

        var names = Directory.EnumerateFileSystemEntries(slicesDir)
            .Select(Path.GetFileName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var discovered = new List<(string Name, List<string> DependsOn)>();
        foreach (var name in names)
        {
            if (name is null) continue;
            var csproj = Path.Combine(slicesDir, name, name + ".csproj");
            if (!File.Exists(csproj)) continue;
            var refs = RefRegex().Matches(File.ReadAllText(csproj))
                .Select(m => m.Groups[1].Value)
                .Where(r => r != kernel)
                .Distinct()
                .OrderBy(r => r, StringComparer.Ordinal)
                .ToList();
            discovered.Add((name, refs));

            var agents = Path.Combine(slicesDir, name, "AGENTS.md");
            var depLine = $"<!--heimdall:deps-->depends on: {(refs.Count > 0 ? string.Join(", ", refs) : "(none)")} + {kernel}<!--/heimdall:deps-->";
            string txt;
            if (File.Exists(agents))
            {
                txt = File.ReadAllText(agents);
                txt = txt.Contains("<!--heimdall:deps-->", StringComparison.Ordinal)
                    ? DepsRegex().Replace(txt, _ => depLine)
                    : txt.TrimEnd() + "\n" + depLine + "\n";
            }
            else
            {
                txt = $"# {name}\n{depLine}\n";
            }
            File.WriteAllText(agents, txt, new UTF8Encoding(false));
        }

        var fanIn = discovered.ToDictionary(s => s.Name, _ => 0, StringComparer.Ordinal);
        foreach (var (_, deps) in discovered)
            foreach (var d in deps)
                if (fanIn.ContainsKey(d)) fanIn[d]++;

        var relSlicesDir = Path.GetRelativePath(cwd, slicesDir);
        var slices = discovered
            .Select(s => new MapSlice(s.Name, Path.GetRelativePath(cwd, Path.Combine(slicesDir, s.Name)), s.DependsOn, budget, fanIn[s.Name]))
            .ToList();

        Directory.CreateDirectory(Path.Combine(cwd, ".heimdall"));
        File.WriteAllText(Path.Combine(cwd, ".heimdall", "map.json"),
            PyJson.MapDocument(kernel, relSlicesDir, slices), new UTF8Encoding(false));
        stdout.WriteLine($"heimdall map: {slices.Count} slices -> .heimdall/map.json; AGENTS.md deps regenerated");
        return 0;
    }
}
