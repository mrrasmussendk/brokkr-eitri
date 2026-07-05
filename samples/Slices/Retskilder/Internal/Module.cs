using SharedKernel;
using Slices.Retskilder.Contract;

namespace Slices.Retskilder.Internal;

public static class Module
{
    public static void Register(IRegistry r) =>
        r.Register("Retskilder", () => new RetskilderService());
}
