using SharedKernel;
using Slices.Domme.Contract;
using Slices.Retskilder.Contract;

namespace Slices.Domme.Internal;

internal sealed class DommeService : IDommeService
{
    private readonly DommeEngine _engine = new();
    private readonly IRetskilderService? _retskilder;

    public DommeService(IRetskilderService? retskilder = null)
    {
        _retskilder = retskilder;
    }

    public Result<DommeAssessment> Assess(CaseId caseId, IReadOnlyList<ProvisionRef> provisions, string factum)
    {
        var boost = 0.0;
        var r0 = _retskilder?.Assess(caseId, provisions, factum);
        if (r0 is { Ok: true, Value: not null }) boost += r0.Value.Score * 0.1;
        var core = _engine.Evaluate(caseId, provisions, factum);
        if (!core.Ok || core.Value is null) return core;
        var v = core.Value;
        return Result<DommeAssessment>.Success(v with { Score = Math.Min(1.0, v.Score + boost) });
    }
}
