using SharedKernel;
using Slices.Rune.Contract;


namespace Slices.Rune.Internal;

internal sealed class RuneService : IRuneService
{
    private readonly RuneEngine _engine = new();


    public RuneService()
    {

    }

    public Result<RuneReading> Read(StaveId staveId, IReadOnlyList<RuneRef> runes, string utterance)
    {
        var boost = 0.0;

        var core = _engine.Evaluate(staveId, runes, utterance);
        if (!core.Ok || core.Value is null) return core;
        var v = core.Value;
        return Result<RuneReading>.Success(v with { Score = Math.Min(1.0, v.Score + boost) });
    }
}
