using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Detour;
using Concord.Emit;
using Concord.Orchestration.Tests.RollbackAssembly;
using Xunit;

namespace Concord.Orchestration.Tests;

[CollectionDefinition(Name)]
public sealed class SharedAssemblyApplyCollection {
    public const string Name = "SharedAssemblyApply";
}

public static class PatcherApplyTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute() {
        return 1;
    }
}

public static class PatcherImperativeTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute() {
        return 1;
    }
}

[Patch(typeof(PatcherApplyTarget))]
public static class PatcherApplyDeclaration {
    [Inject(At.Head, nameof(PatcherApplyTarget.Compute))]
    public static void OnCompute(ControlHandle<int> ch) {
        ch.ReturnValue = 42;
        ch.Cancel();
    }
}

public static class PatcherImperativeInjectionMethod {
    public static void OnCompute(ControlHandle<int> ch) {
        ch.ReturnValue = 99;
        ch.Cancel();
    }
}

public static class ConcurrentTargetA {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Value() {
        return 1;
    }
}

public static class ConcurrentTargetB {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Value() {
        return 2;
    }
}

public static class ConcurrentTargetC {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Value() {
        return 3;
    }
}

public static class ConcurrentTargetD {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Value() {
        return 4;
    }
}

public static class PatcherGcTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute() {
        return 1;
    }
}

public static class PatcherGcInjectionMethod {
    public static void OnCompute(ControlHandle<int> ch) {
        ch.ReturnValue = 99;
        ch.Cancel();
    }
}

public static class PatcherReturnTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute() {
        return 1;
    }
}

public static class PatcherReturnInjectionMethod {
    public static void OnReturn(ControlHandle<int> ch) {
        ch.ReturnValue = 99;
    }
}

[Collection(SharedAssemblyApplyCollection.Name)]
public sealed class PatcherTests : IDisposable {
    private readonly RecordingDetourBackend backend = new RecordingDetourBackend();

    public PatcherTests() {
        DetourBackend.Current = backend;
    }

    public void Dispose() {
        DetourBackend.Reset();
    }

    [Fact]
    public void Apply_AppliesEveryDeclaredInjection_DisposeRemovesThem() {
        MethodBase target = typeof(PatcherApplyTarget).GetMethod(nameof(PatcherApplyTarget.Compute))!;

        IPatchHandle handle = Patcher.Apply(typeof(PatcherApplyDeclaration).Assembly);
        try {
            Assert.True(handle.IsApplied);
            Assert.NotEmpty(handle.Detours);
            Assert.Equal([typeof(PatcherApplyDeclaration).FullName!], backend.OwnersOf(target));
        } finally {
            handle.Dispose();
        }

        Assert.False(handle.IsApplied);
        Assert.Empty(backend.OwnersOf(target));
    }

    [Fact]
    public void Apply_Twice_SameAssembly_ReturnsSameHandle_NoDoubleApply() {
        IPatchHandle first = Patcher.Apply(typeof(PatcherApplyDeclaration).Assembly);
        try {
            int appliedOnce = backend.Applied.Count;

            IPatchHandle second = Patcher.Apply(typeof(PatcherApplyDeclaration).Assembly);

            Assert.Same(first, second);
            Assert.Equal(appliedOnce, backend.Applied.Count);
        } finally {
            first.Dispose();
        }
    }

    [Fact]
    public void Apply_WhenScanThrows_RevertsAlreadyAppliedDetours() {
        Assembly rollbackAssembly = typeof(RollbackGoodTarget).Assembly;
        MethodBase good = typeof(RollbackGoodTarget).GetMethod(nameof(RollbackGoodTarget.Compute))!;
        backend.FailOn = (_, injection) =>
            injection.InjectionMethod.DeclaringType == typeof(RollbackBadDeclaration)
                ? new ConcordEmitException("CONC012", "cancel without a return value")
                : null;

        Assert.Throws<ConcordEmitException>(() => Patcher.Apply(rollbackAssembly));

        Assert.Contains(backend.Applied, entry => Equals(entry.Target, good));
        Assert.Equal(0, backend.PatchedTargetCount);
    }

    [Fact]
    public void Patch_SingleExplicit_ReachesTheBackendAtTheRequestedPosition() {
        MethodBase target = typeof(PatcherImperativeTarget).GetMethod(nameof(PatcherImperativeTarget.Compute))!;
        MethodBase injectionMethod = typeof(PatcherImperativeInjectionMethod).GetMethod(nameof(PatcherImperativeInjectionMethod.OnCompute))!;

        IPatchHandle handle = Patcher.Patch(target, injectionMethod, At.Head);
        try {
            AppliedInjection applied = Assert.Single(backend.Applied);

            Assert.Equal(target, applied.Target);
            Assert.Equal(injectionMethod, applied.Injection.InjectionMethod);
            Assert.IsType<InjectAt.Head>(applied.Injection.At);
            Assert.Equal(typeof(PatcherImperativeInjectionMethod).FullName, applied.Injection.Owner);
        } finally {
            handle.Dispose();
        }

        Assert.Empty(backend.OwnersOf(target));
    }

    [Fact]
    public void PatchInjection_HandsTheInjectionToTheBackendUnchanged() {
        MethodBase target = typeof(PatcherReturnTarget).GetMethod(nameof(PatcherReturnTarget.Compute))!;
        MethodBase injectionMethod = typeof(PatcherReturnInjectionMethod).GetMethod(nameof(PatcherReturnInjectionMethod.OnReturn))!;
        Injection injection = new Injection((MethodInfo)injectionMethod, new InjectAt.Return(0), injectionMethod.DeclaringType!.FullName!, 0);

        IPatchHandle handle = Patcher.PatchInjection(target, injection);
        try {
            Assert.True(handle.IsApplied);

            AppliedInjection applied = Assert.Single(backend.Applied);
            Assert.Equal(target, applied.Target);
            Assert.Same(injection, applied.Injection);
        } finally {
            handle.Dispose();
        }

        Assert.Empty(backend.OwnersOf(target));
    }

    [Fact]
    public void Patch_DiscardedHandle_SurvivesGarbageCollection() {
        MethodBase target = typeof(PatcherGcTarget).GetMethod(nameof(PatcherGcTarget.Compute))!;

        ApplyAndDiscard();

        for (int i = 0; i < 3; i++) {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);
            GC.WaitForPendingFinalizers();
        }

        Assert.Equal([typeof(PatcherGcInjectionMethod).FullName!], backend.OwnersOf(target));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ApplyAndDiscard() {
        MethodBase target = typeof(PatcherGcTarget).GetMethod(nameof(PatcherGcTarget.Compute))!;
        MethodBase injectionMethod = typeof(PatcherGcInjectionMethod).GetMethod(nameof(PatcherGcInjectionMethod.OnCompute))!;
        Patcher.Patch(target, injectionMethod, At.Head);
    }

    [Fact]
    public void Patch_InjectionMethodWithNoDeclaringType_ThrowsConcordDeclarationException() {
        MethodBase target = typeof(PatcherImperativeTarget).GetMethod(nameof(PatcherImperativeTarget.Compute))!;
        System.Reflection.Emit.DynamicMethod dynamic = new System.Reflection.Emit.DynamicMethod("orphan", typeof(void), Type.EmptyTypes);

        Assert.Throws<ConcordDeclarationException>(() => Patcher.Patch(target, dynamic, At.Head));
    }

    [Fact]
    public void ConcurrentPatch_DifferentTargets_AllApply() {
        MethodBase[] targets = [
            typeof(ConcurrentTargetA).GetMethod(nameof(ConcurrentTargetA.Value))!,
            typeof(ConcurrentTargetB).GetMethod(nameof(ConcurrentTargetB.Value))!,
            typeof(ConcurrentTargetC).GetMethod(nameof(ConcurrentTargetC.Value))!,
            typeof(ConcurrentTargetD).GetMethod(nameof(ConcurrentTargetD.Value))!
        ];
        MethodBase injection = typeof(PatcherImperativeInjectionMethod).GetMethod(nameof(PatcherImperativeInjectionMethod.OnCompute))!;

        IPatchHandle[] handles = new IPatchHandle[targets.Length];
        System.Threading.Tasks.Parallel.For(0, targets.Length, i => handles[i] = Patcher.Patch(targets[i], injection, At.Head));
        try {
            Assert.All(handles, h => Assert.True(h.IsApplied));
        } finally {
            foreach (IPatchHandle h in handles) {
                h.Dispose();
            }
        }
    }
}
