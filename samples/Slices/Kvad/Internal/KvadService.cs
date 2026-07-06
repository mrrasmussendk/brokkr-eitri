using SharedKernel;
using Slices.Kvad.Contract;
using Slices.Rune.Contract;

namespace Slices.Kvad.Internal;

internal sealed class KvadService : IKvadService
{
    private readonly KvadEngine _engine = new();
    private readonly IRuneService? _rune;

    public KvadService(IRuneService? rune = null)
    {
        _rune = rune;
    }

    public Result<Verse> Compose(StaveId staveId, IReadOnlyList<RuneRef> runes, string utterance)
    {
        var boost = 0.0;
        var r0 = _rune?.Read(staveId, runes, utterance);
        if (r0 is { Ok: true, Value: not null }) boost += r0.Value.Score * 0.1;
        var core = _engine.Evaluate(staveId, runes, utterance);
        if (!core.Ok || core.Value is null) return core;
        var v = core.Value;
        return Result<Verse>.Success(v with { Score = Math.Min(1.0, v.Score + boost) });
    }
}
