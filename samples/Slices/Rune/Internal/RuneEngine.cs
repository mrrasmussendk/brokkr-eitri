using SharedKernel;
using Slices.Rune.Contract;

namespace Slices.Rune.Internal;

internal sealed class RuneEngine
{

    public Result<RuneReading> Evaluate(StaveId staveId, IReadOnlyList<RuneRef> runes, string utterance)
    {
        if (string.IsNullOrWhiteSpace(utterance))
            return Result<RuneReading>.Failure("empty utterance");

        var weight = 0.0;
        var notes = new List<string>(runes.Count);
        foreach (var r in runes)
        {
            var w = ScoreRune(r, utterance);
            weight += w;
            if (w > 0.5) notes.Add($"{r} rings clear ({w:F2})");
            else if (w > 0.2) notes.Add($"{r} half-sounded ({w:F2})");
        }

        var normalized = runes.Count == 0 ? 0 : weight / runes.Count;
        var clarity = normalized switch
        {
            > 0.75 => Clarity.Luminous,
            > 0.45 => Clarity.Clear,
            > 0.15 => Clarity.Worn,
            _ => Clarity.Faint,
        };
        return Result<RuneReading>.Success(new RuneReading(staveId, clarity, normalized, notes));
    }

    private static double ScoreRune(RuneRef r, string utterance)
    {
        var score = 0.0;
        var terms = utterance.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var t in terms)
        {
            var h = 0;
            foreach (var c in t) h = unchecked(h * 31 + c);
            var pv = 0;
            foreach (var c in r.Mark) pv = unchecked(pv * 31 + c);
            if (((h ^ pv) & 7) == 0) score += 0.13;
            if (t.Length > 8 && r.Row.Length > 4 && (t.Length + r.Row.Length) % 5 == 0) score += 0.05;
        }
        return Math.Min(1.0, score);
    }

}
