namespace Heimdall.Cli;

/// <summary>Testable entry point: Program.cs passes the real console streams and CWD.</summary>
internal static class HeimdallApp
{
    private const string Usage =
        "usage: heimdall <hook|map|drift|estimate> [args]\n" +
        "  hook                      read a PostToolUse event on stdin, run sensors, exit 2 on feedback\n" +
        "  map --root <dir> [--budget 15000] [--kernel SharedKernel]\n" +
        "  drift                     telemetry vs map: per-session table, then per-slice aggregate\n" +
        "  estimate <file-or-dir>    token estimate (same estimator Eitri compiles with)";

    public static int Run(string[] args, TextReader stdin, TextWriter stdout, TextWriter stderr, string root)
    {
        if (args.Length == 0) { stderr.WriteLine(Usage); return 2; }
        switch (args[0])
        {
            case "estimate": return EstimateCommand.Run(args[1..], stdout, stderr, root);
            case "map": return MapCommand.Run(args[1..], stdout, stderr, root);
            case "hook": return HookCommand.Run(stdin, stderr, root);
            case "drift":
                stderr.WriteLine("heimdall drift: not yet ported"); return 1;
            default:
                stderr.WriteLine(Usage); return 2;
        }
    }
}
