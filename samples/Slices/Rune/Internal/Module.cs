using SharedKernel;
using Slices.Rune.Contract;

namespace Slices.Rune.Internal;

public static class Module
{
    public static void Register(IRegistry r) =>
        r.Register("Rune", () => new RuneService());
}
