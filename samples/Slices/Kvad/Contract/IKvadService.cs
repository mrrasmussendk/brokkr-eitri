using SharedKernel;

namespace Slices.Kvad.Contract;

public enum Clarity { Faint, Worn, Clear, Luminous }

public sealed record Verse(
    StaveId StaveId, Clarity Clarity, double Score, IReadOnlyList<string> Notes);

public interface IKvadService
{
    Result<Verse> Compose(StaveId staveId, IReadOnlyList<RuneRef> runes, string utterance);
}
