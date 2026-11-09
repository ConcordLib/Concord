using System.Reflection;
using Concord.Emit;
using Xunit;

namespace Concord.MonoMod.Tests;

public sealed class ScratchGeneratorInstallTests {
    [Fact]
    public void Install_OverAGeneratorFromAnotherCopy_TakesOverTheEmitRoute() {
        IScratchGenerator? installed = ScratchGenerator.Current;
        OtherCopysGenerator other = new OtherCopysGenerator();

        try {
            ScratchGenerator.Current = other;

            MonoModEmit.InstallScratchGenerator();

            Assert.NotSame(other, ScratchGenerator.Current);
            Assert.IsType<MonoModScratchGenerator>(ScratchGenerator.Current);
        } finally {
            ScratchGenerator.Current = installed;
        }
    }

    private sealed class OtherCopysGenerator : IScratchGenerator {
        public IScratchBody Begin(string name, Type returnType, Type[] parameterTypes, MethodBase origin) {
            throw new NotSupportedException(name);
        }
    }
}
