using SharedKernel;
using Slices.Kvad.Contract;
using Slices.Rune.Contract;

namespace Slices.Kvad.Internal;

public static class Module
{
    public static void Register(IRegistry r) =>
        r.Register("Kvad", () => new KvadService((IRuneService)r.Resolve("Rune")));
}
