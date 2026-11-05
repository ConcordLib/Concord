using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.AttachedData;
using Xunit;

namespace Concord.Emit.Tests;

public class AttachedSeedTarget {
    public int observed;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Compute() {
        return 1;
    }
}

#pragma warning disable CS0109, CS0414
public class AttachedUnmarkedInjectionMethod : AttachedSeedTarget {
    public int notOnTarget;

    public void Head(ControlHandle<int> ch) {
        ch.ReturnValue = 42;
        ch.Cancel();
    }
}

public class AttachedMismatchShadowInjectionMethod : AttachedSeedTarget {
    private new long observed;

    public void Head(ControlHandle<int> ch) {
        observed = 7L;
        ch.ReturnValue = 5;
        ch.Cancel();
    }
}
#pragma warning restore CS0109, CS0414

public sealed class ShadowAttachedFieldTests {
    [Fact]
    public void Compose_UnmarkedFieldAbsentOnTarget_ThrowsCONC003() {
        MethodBase target = typeof(AttachedSeedTarget).GetMethod(nameof(AttachedSeedTarget.Compute))!;
        MethodBase injectionMethod = typeof(AttachedUnmarkedInjectionMethod).GetMethod(nameof(AttachedUnmarkedInjectionMethod.Head))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => WrapperComposer.Compose(target, [head]));

        Assert.Equal("CONC003", ex.Code);
    }

    [Fact]
    public void SlotFor_SameNameDifferentValueType_Throws() {
        AttachedStorage.SlotFor(typeof(AttachedSeedTarget), "reallocated", typeof(int));

        Assert.Throws<InvalidOperationException>(() =>
            AttachedStorage.SlotFor(typeof(AttachedSeedTarget), "reallocated", typeof(long)));
    }

    [Fact]
    public void Compose_MismatchedShadow_ThrowsCONC002() {
        MethodBase target = typeof(AttachedSeedTarget).GetMethod(nameof(AttachedSeedTarget.Compute))!;
        MethodBase injectionMethod = typeof(AttachedMismatchShadowInjectionMethod).GetMethod(nameof(AttachedMismatchShadowInjectionMethod.Head))!;
        Injection head = new Injection(injectionMethod, new InjectAt.Head(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            WrapperComposer.Compose(target, [head]));

        Assert.Equal("CONC002", ex.Code);
    }
}
