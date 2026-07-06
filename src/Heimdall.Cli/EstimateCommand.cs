using System.Globalization;
using Brokkr.Tokenization;

namespace Heimdall.Cli;

/// <summary>
/// `heimdall estimate <file-or-dir>` — prints the token estimate as a bare integer.
/// Directories walk *.cs recursively (what EIT100 budgets). Same linked TokenEstimator
/// source as Eitri.Analyzers, so the number cannot drift from the compile-time gate.
/// </summary>
internal static class EstimateCommand
{
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, string cwd)
    {
        if (args.Length != 1) { stderr.WriteLine("usage: heimdall estimate <file-or-dir>"); return 2; }
        var target = Path.GetFullPath(args[0], cwd);
        if (File.Exists(target))
        {
            stdout.WriteLine(TokenEstimator.Estimate(File.ReadAllText(target)).ToString(CultureInfo.InvariantCulture));
            return 0;
        }
        if (Directory.Exists(target))
        {
            long total = 0;
            foreach (var f in Directory.EnumerateFiles(target, "*.cs", SearchOption.AllDirectories))
                total += TokenEstimator.Estimate(File.ReadAllText(f));
            stdout.WriteLine(total.ToString(CultureInfo.InvariantCulture));
            return 0;
        }
        stderr.WriteLine($"heimdall estimate: no such file or directory: {args[0]}");
        return 1;
    }
}
