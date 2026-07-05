using SharedKernel;
using Slices.Retskilder.Contract;


namespace Slices.Retskilder.Internal;

internal sealed class RetskilderService : IRetskilderService
{
    private readonly RetskilderEngine _engine = new();


    public RetskilderService()
    {

    }

    public Result<RetskilderAssessment> Assess(CaseId caseId, IReadOnlyList<ProvisionRef> provisions, string factum)
    {
        var boost = 0.0;

        var core = _engine.Evaluate(caseId, provisions, factum);
        if (!core.Ok || core.Value is null) return core;
        var v = core.Value;
        return Result<RetskilderAssessment>.Success(v with { Score = Math.Min(1.0, v.Score + boost) });
    }
}
