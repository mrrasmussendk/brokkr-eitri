using SharedKernel;
using Slices.Domme.Contract;
using Slices.Retskilder.Contract;

namespace Slices.Domme.Internal;

public static class Module
{
    public static void Register(IRegistry r) =>
        r.Register("Domme", () => new DommeService((IRetskilderService)r.Resolve("Retskilder")));
}
