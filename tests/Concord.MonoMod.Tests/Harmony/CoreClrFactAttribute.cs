using Xunit;

namespace Concord.MonoMod.Tests;

public sealed class CoreClrFactAttribute : FactAttribute
{
    public CoreClrFactAttribute(string monoReason)
    {
#if NET472
        Skip = monoReason;
#endif
    }
}
