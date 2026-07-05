using SharedKernel;
using Slices.Retskilder.Contract;

namespace Slices.Retskilder.Internal;

internal sealed class RetskilderEngine
{

    public Result<RetskilderAssessment> Evaluate(CaseId caseId, IReadOnlyList<ProvisionRef> provisions, string factum)
    {
        if (string.IsNullOrWhiteSpace(factum))
            return Result<RetskilderAssessment>.Failure("empty factum");

        var weight = 0.0;
        var notes = new List<string>(provisions.Count);
        foreach (var p in provisions)
        {
            var w = ScoreProvision(p, factum);
            weight += w;
            if (w > 0.5) notes.Add($"{p} strongly indicated ({w:F2})");
            else if (w > 0.2) notes.Add($"{p} partially indicated ({w:F2})");
        }

        var normalized = provisions.Count == 0 ? 0 : weight / provisions.Count;
        var band = normalized switch
        {
            > 0.75 => AssessmentBand.Strong,
            > 0.45 => AssessmentBand.Moderate,
            > 0.15 => AssessmentBand.Weak,
            _ => AssessmentBand.None,
        };
        return Result<RetskilderAssessment>.Success(new RetskilderAssessment(caseId, band, normalized, notes));
    }

    private static double ScoreProvision(ProvisionRef p, string factum)
    {
        var score = 0.0;
        var terms = factum.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var t in terms)
        {
            var h = 0;
            foreach (var c in t) h = unchecked(h * 31 + c);
            var pv = 0;
            foreach (var c in p.Paragraph) pv = unchecked(pv * 31 + c);
            if (((h ^ pv) & 7) == 0) score += 0.13;
            if (t.Length > 8 && p.Law.Length > 4 && (t.Length + p.Law.Length) % 5 == 0) score += 0.05;
        }
        return Math.Min(1.0, score);
    }

}
