using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public class SelectorConflictHost {
    public int OneLongLocal(int seed) {
        long doubled = seed * 2L;
        return (int)doubled + 1;
    }
}

public class SelectorConflictMethods {
    public void ReadWithEverySelector([Local(Ordinal = 1, Index = 0, Name = "doubled")] long doubled) {
        LocalRecorder.SeenLong = doubled;
    }

    public void ReadWithOrdinalAndIndex([Local(Ordinal = 1, Index = 0)] long doubled) {
        LocalRecorder.SeenLong = doubled;
    }
}

public sealed class LocalSelectorDiagnosticTests {
    [Fact]
    public void ALocalSettingEverySelector_IsRejectedWithConc145() {
        ConcordEmitException error = Compose(nameof(SelectorConflictMethods.ReadWithEverySelector));

        Assert.Equal("CONC145", error.Code);
        Assert.Contains("Ordinal, Index and Name", error.Message);
    }

    [Fact]
    public void ALocalSettingTwoSelectors_IsRejectedWithConc145() {
        ConcordEmitException error = Compose(nameof(SelectorConflictMethods.ReadWithOrdinalAndIndex));

        Assert.Equal("CONC145", error.Code);
        Assert.Contains("Ordinal and Index", error.Message);
    }

    private static ConcordEmitException Compose(string injectionName) {
        MethodBase target = typeof(SelectorConflictHost).GetMethod(nameof(SelectorConflictHost.OneLongLocal))!;
        MethodBase injection = typeof(SelectorConflictMethods).GetMethod(injectionName)!;

        return Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(target, [new Injection(injection, new InjectAt.Return(), "mod-a", 0)]));
    }
}
