using SharedKernel;

namespace Slices.Rune.Contract;

public enum Clarity { Faint, Worn, Clear, Luminous }

public sealed record RuneReading(
    StaveId StaveId, Clarity Clarity, double Score, IReadOnlyList<string> Notes);

public interface IRuneService
{
    Result<RuneReading> Read(StaveId staveId, IReadOnlyList<RuneRef> runes, string utterance);
    int SchemaVersion => 2;   // additive: default interface method
}
