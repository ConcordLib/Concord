using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Concord.Emit;
using Xunit;

namespace Concord.Orchestration.Tests;

public static class BuilderMethodBaseTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute() {
        return 1;
    }
}

public static class BuilderTypeTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute() {
        return 1;
    }
}

public sealed class BuilderGenericTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute() {
        return 1;
    }
}

public static class BuilderStringTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute() {
        return 1;
    }
}

public static class BuilderGcTarget2 {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute() {
        return 1;
    }
}

public static class BuilderReturnTarget2 {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute() {
        return 1;
    }
}

public static class BuilderAroundTarget2 {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Step() {
        BuilderLog.Entries.Add("spine");
    }
}

public static class BuilderTranspilerTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute() {
        return 5;
    }
}

public static class BuilderTranspilerFinalTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute() {
        return 5;
    }
}

public static class BuilderTranspilerChainTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute() {
        return 5;
    }
}

public static class BuilderTranspilers {
    public static IEnumerable<CodeInstruction> FiveToTen(IEnumerable<CodeInstruction> instructions) {
        foreach (CodeInstruction instruction in instructions) {
            if (instruction.opcode == OpCodes.Ldc_I4_5) {
                yield return new CodeInstruction(OpCodes.Ldc_I4, 10);
            } else {
                yield return instruction;
            }
        }
    }

    public static IEnumerable<CodeInstruction> TenToTwenty(IEnumerable<CodeInstruction> instructions) {
        foreach (CodeInstruction instruction in instructions) {
            if (instruction.Is(OpCodes.Ldc_I4, 10)) {
                yield return new CodeInstruction(OpCodes.Ldc_I4, 20);
            } else {
                yield return instruction;
            }
        }
    }
}

public static class BuilderLog {
    public static List<string> Entries = new List<string>();
}

public static class BuilderInvokeHelper2 {
    public static void Step() {
        BuilderLog.Entries.Add("step");
    }
}

public class BuilderInvokeTarget2 {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Run() {
        BuilderLog.Entries.Add("before");
        BuilderInvokeHelper2.Step();
        BuilderLog.Entries.Add("after");
    }
}

public static class BuilderSliceAnchors2 {
    public static void Begin() { }

    public static void End() { }
}

internal class BuilderSliceTarget2 {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Run() {
        BuilderInvokeHelper2.Step();
        BuilderSliceAnchors2.Begin();
        BuilderInvokeHelper2.Step();
        BuilderSliceAnchors2.End();
        BuilderInvokeHelper2.Step();
    }
}

public static class BuilderHeadInjectionMethod2 {
    public static void OnCompute(ControlHandle<int> ch) {
        ch.ReturnValue = 99;
        ch.Cancel();
    }
}

public static class BuilderGcInjectionMethod2 {
    public static void OnCompute(ControlHandle<int> ch) {
        ch.ReturnValue = 99;
        ch.Cancel();
    }
}

public static class BuilderReturnInjectionMethod2 {
    public static void OnCompute(ControlHandle<int> ch) {
        ch.ReturnValue = 77;
    }
}

public static class BuilderAroundInjectionMethod2 {
    public static void OnStep(Operation original) {
        BuilderLog.Entries.Add("pre");
        original.Invoke();
        BuilderLog.Entries.Add("post");
    }
}

public static class BuilderInvokeInjectionMethod2 {
    public static void BeforeStep(ControlHandle ch) {
        BuilderLog.Entries.Add("injected");
    }
}

public static class BuilderNewObjInjectionMethod2 {
    public static int Bump(int original) {
        return original + 1;
    }
}

public static class BuilderNewObjAnchors3 {
    public static void Begin() { }

    public static void End() { }
}

public sealed class BuilderNewObjValue3 {
    public BuilderNewObjValue3(int value) {
        Value = value;
    }

    public int Value { get; }
}

public class BuilderNewObjTarget3 {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int[] Run() {
        BuilderNewObjValue3 first = new BuilderNewObjValue3(1);
        BuilderNewObjAnchors3.Begin();
        BuilderNewObjValue3 second = new BuilderNewObjValue3(2);
        BuilderNewObjAnchors3.End();
        BuilderNewObjValue3 third = new BuilderNewObjValue3(3);
        return [first.Value, second.Value, third.Value];
    }
}

[Collection(SharedAssemblyApplyCollection.Name)]
public sealed class PatchBuilderTests : IDisposable {
    private readonly RecordingDetourBackend backend = new RecordingDetourBackend();

    public PatchBuilderTests() {
        Concord.Backend.PatchBackend.Current = backend;
    }

    public void Dispose() {
        Concord.Backend.PatchBackend.Reset();
    }

    [Fact]
    public void For_MethodBase_ResolvesTheTarget() {
        MethodBase target = typeof(BuilderMethodBaseTarget).GetMethod(nameof(BuilderMethodBaseTarget.Compute))!;
        MethodInfo injectionMethod = typeof(BuilderHeadInjectionMethod2).GetMethod(nameof(BuilderHeadInjectionMethod2.OnCompute))!;

        using IPatchHandle handle = Patcher.For(target).Head(injectionMethod).Apply();

        AppliedInjection applied = Assert.Single(backend.Applied);
        Assert.Equal(target, applied.Target);
        Assert.IsType<InjectAt.Head>(applied.Injection.At);
    }

    [Fact]
    public void For_TypeAndName_ResolvesTheTarget() {
        MethodInfo injectionMethod = typeof(BuilderHeadInjectionMethod2).GetMethod(nameof(BuilderHeadInjectionMethod2.OnCompute))!;

        using IPatchHandle handle = Patcher.For(typeof(BuilderTypeTarget), nameof(BuilderTypeTarget.Compute))
            .Head(injectionMethod)
            .Apply();

        Assert.Equal(
            typeof(BuilderTypeTarget).GetMethod(nameof(BuilderTypeTarget.Compute)),
            Assert.Single(backend.Applied).Target);
    }

    [Fact]
    public void For_Generic_ResolvesTheTarget() {
        MethodInfo injectionMethod = typeof(BuilderHeadInjectionMethod2).GetMethod(nameof(BuilderHeadInjectionMethod2.OnCompute))!;

        using IPatchHandle handle = Patcher.For<BuilderGenericTarget>(nameof(BuilderGenericTarget.Compute))
            .Head(injectionMethod)
            .Apply();

        Assert.Equal(
            typeof(BuilderGenericTarget).GetMethod(nameof(BuilderGenericTarget.Compute)),
            Assert.Single(backend.Applied).Target);
    }

    [Fact]
    public void For_StringTypeName_ResolvesTheTarget() {
        MethodInfo injectionMethod = typeof(BuilderHeadInjectionMethod2).GetMethod(nameof(BuilderHeadInjectionMethod2.OnCompute))!;
        string typeName = typeof(BuilderStringTarget).FullName!;

        using IPatchHandle handle = Patcher.For(typeName, nameof(BuilderStringTarget.Compute))
            .Head(injectionMethod)
            .Apply();

        Assert.Equal(
            typeof(BuilderStringTarget).GetMethod(nameof(BuilderStringTarget.Compute)),
            Assert.Single(backend.Applied).Target);
    }

    [Fact]
    public void Apply_DiscardedHandle_SurvivesGarbageCollection() {
        MethodBase target = typeof(BuilderGcTarget2).GetMethod(nameof(BuilderGcTarget2.Compute))!;

        ApplyAndDiscardViaBuilder();

        for (int i = 0; i < 3; i++) {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);
            GC.WaitForPendingFinalizers();
        }

        Assert.Equal([typeof(BuilderGcInjectionMethod2).FullName!], backend.OwnersOf(target));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ApplyAndDiscardViaBuilder() {
        MethodBase target = typeof(BuilderGcTarget2).GetMethod(nameof(BuilderGcTarget2.Compute))!;
        MethodInfo injectionMethod = typeof(BuilderGcInjectionMethod2).GetMethod(nameof(BuilderGcInjectionMethod2.OnCompute))!;
        Patcher.For(target).Head(injectionMethod).Apply();
    }

    [Fact]
    public void Head_MethodInfo_And_TypeName_AreEquivalent() {
        MethodInfo injectionMethod = typeof(BuilderHeadInjectionMethod2).GetMethod(nameof(BuilderHeadInjectionMethod2.OnCompute))!;

        using IPatchHandle fromMethodInfo = Patcher.For(typeof(BuilderTypeTarget), nameof(BuilderTypeTarget.Compute))
            .Head(injectionMethod)
            .Apply();
        using IPatchHandle fromTypeName = Patcher.For(typeof(BuilderTypeTarget), nameof(BuilderTypeTarget.Compute))
            .Head(typeof(BuilderHeadInjectionMethod2), nameof(BuilderHeadInjectionMethod2.OnCompute))
            .Apply();

        Assert.Equal(2, backend.Applied.Count);
        Assert.Equal(backend.Applied[0].Target, backend.Applied[1].Target);
        Assert.Equal(backend.Applied[0].Injection, backend.Applied[1].Injection);
    }

    [Fact]
    public void Inject_AtHead_IsEquivalentTo_Head() {
        MethodInfo injectionMethod = typeof(BuilderHeadInjectionMethod2).GetMethod(nameof(BuilderHeadInjectionMethod2.OnCompute))!;

        using IPatchHandle fromInject = Patcher.For(typeof(BuilderTypeTarget), nameof(BuilderTypeTarget.Compute))
            .Inject(At.Head, injectionMethod)
            .Apply();
        using IPatchHandle fromHead = Patcher.For(typeof(BuilderTypeTarget), nameof(BuilderTypeTarget.Compute))
            .Head(injectionMethod)
            .Apply();

        Assert.Equal(2, backend.Applied.Count);
        Assert.Equal(backend.Applied[0].Injection, backend.Applied[1].Injection);
    }

    [Fact]
    public void Return_InjectionMethod_RegistersAtTheReturnPosition() {
        MethodInfo injectionMethod = typeof(BuilderReturnInjectionMethod2).GetMethod(nameof(BuilderReturnInjectionMethod2.OnCompute))!;

        using IPatchHandle handle = Patcher.For(typeof(BuilderReturnTarget2), nameof(BuilderReturnTarget2.Compute))
            .Return(injectionMethod)
            .Apply();

        Assert.IsType<InjectAt.Return>(Assert.Single(backend.Applied).Injection.At);
    }

    [Fact]
    public void Around_InjectionMethod_RegistersAtTheAroundPosition() {
        MethodInfo injectionMethod = typeof(BuilderAroundInjectionMethod2).GetMethod(nameof(BuilderAroundInjectionMethod2.OnStep))!;

        using IPatchHandle handle = Patcher.For(typeof(BuilderAroundTarget2), nameof(BuilderAroundTarget2.Step))
            .Around(injectionMethod)
            .Apply();

        AppliedInjection applied = Assert.Single(backend.Applied);
        Assert.IsType<InjectAt.Around>(applied.Injection.At);
        Assert.Equal(injectionMethod, applied.Injection.InjectionMethod);
    }

    [Fact]
    public void Invoke_Builder_RegistersTheCallSite() {
        MethodBase targetMethod = typeof(BuilderInvokeTarget2).GetMethod(nameof(BuilderInvokeTarget2.Run))!;
        MethodInfo injectionMethod = typeof(BuilderInvokeInjectionMethod2).GetMethod(nameof(BuilderInvokeInjectionMethod2.BeforeStep))!;

        using IPatchHandle handle = Patcher.For(targetMethod)
            .Invoke(typeof(BuilderInvokeHelper2), nameof(BuilderInvokeHelper2.Step), injectionMethod, At.Head)
            .Apply();

        AppliedInjection applied = Assert.Single(backend.Applied);
        InjectAt.Invoke at = Assert.IsType<InjectAt.Invoke>(applied.Injection.At);

        Assert.Equal(targetMethod, applied.Target);
        Assert.Equal(typeof(BuilderInvokeHelper2), at.DeclaringType);
        Assert.Equal(nameof(BuilderInvokeHelper2.Step), at.Method);
        Assert.Equal(At.Head, at.Shift);
    }

    [Fact]
    public void NewObj_Builder_Slice_AttachesTheRangeToTheConstruction() {
        MethodBase targetMethod = typeof(BuilderNewObjTarget3).GetMethod(nameof(BuilderNewObjTarget3.Run))!;
        MethodInfo injectionMethod = typeof(BuilderNewObjInjectionMethod2).GetMethod(nameof(BuilderNewObjInjectionMethod2.Bump))!;
        SliceRange range = new SliceRange(
            typeof(BuilderNewObjAnchors3),
            nameof(BuilderNewObjAnchors3.Begin),
            1,
            typeof(BuilderNewObjAnchors3),
            nameof(BuilderNewObjAnchors3.End),
            1);

        using IPatchHandle handle = Patcher.For(targetMethod)
            .NewObj(typeof(BuilderNewObjValue3), [typeof(int)], injectionMethod, At.Argument)
            .Slice(range)
            .Apply();

        InjectAt.NewObj at = Assert.IsType<InjectAt.NewObj>(Assert.Single(backend.Applied).Injection.At);
        Assert.Equal(typeof(BuilderNewObjValue3), at.ConstructedType);
        Assert.Equal(At.Argument, at.Shift);
        Assert.Equal(range, at.Slice);
    }

    [Fact]
    public void Slice_Builder_BoundsMostRecentInvokeInjection() {
        MethodBase targetMethod = typeof(BuilderSliceTarget2).GetMethod(nameof(BuilderSliceTarget2.Run))!;
        MethodInfo injectionMethod = typeof(BuilderInvokeInjectionMethod2).GetMethod(nameof(BuilderInvokeInjectionMethod2.BeforeStep))!;
        SliceRange range = new SliceRange(
            typeof(BuilderSliceAnchors2),
            nameof(BuilderSliceAnchors2.Begin),
            1,
            typeof(BuilderSliceAnchors2),
            nameof(BuilderSliceAnchors2.End),
            1);

        using IPatchHandle handle = Patcher.For(targetMethod)
            .Invoke(typeof(BuilderInvokeHelper2), nameof(BuilderInvokeHelper2.Step), injectionMethod, At.Head)
            .Slice(range)
            .Apply();

        InjectAt.Invoke at = Assert.IsType<InjectAt.Invoke>(Assert.Single(backend.Applied).Injection.At);
        Assert.Equal(range, at.Slice);
    }

    [Fact]
    public void Transpiler_MethodInfo_RegistersANonFinalTranspiler() {
        MethodBase target = typeof(BuilderTranspilerTarget).GetMethod(nameof(BuilderTranspilerTarget.Compute))!;
        MethodInfo transpiler = typeof(BuilderTranspilers).GetMethod(nameof(BuilderTranspilers.FiveToTen))!;

        using IPatchHandle handle = Patcher.For(target).Transpiler(transpiler).Apply();

        AppliedInjection applied = Assert.Single(backend.Applied);
        Assert.Equal(transpiler, applied.Injection.InjectionMethod);
        Assert.False(Assert.IsType<InjectAt.Transpiler>(applied.Injection.At).Final);
    }

    [Fact]
    public void Transpiler_TypeAndName_RegistersANonFinalTranspiler() {
        MethodBase target = typeof(BuilderTranspilerTarget).GetMethod(nameof(BuilderTranspilerTarget.Compute))!;

        using IPatchHandle handle = Patcher.For(target)
            .Transpiler(typeof(BuilderTranspilers), nameof(BuilderTranspilers.FiveToTen))
            .Apply();

        AppliedInjection applied = Assert.Single(backend.Applied);
        Assert.Equal(typeof(BuilderTranspilers).GetMethod(nameof(BuilderTranspilers.FiveToTen)), applied.Injection.InjectionMethod);
        Assert.False(Assert.IsType<InjectAt.Transpiler>(applied.Injection.At).Final);
    }

    [Fact]
    public void Transpiler_Final_MethodInfo_RegistersAFinalTranspiler() {
        MethodBase target = typeof(BuilderTranspilerFinalTarget).GetMethod(nameof(BuilderTranspilerFinalTarget.Compute))!;
        MethodInfo transpiler = typeof(BuilderTranspilers).GetMethod(nameof(BuilderTranspilers.FiveToTen))!;

        using IPatchHandle handle = Patcher.For(target).Transpiler(transpiler, final: true).Apply();

        Assert.True(Assert.IsType<InjectAt.Transpiler>(Assert.Single(backend.Applied).Injection.At).Final);
    }

    [Fact]
    public void Transpiler_Final_TypeAndName_RegistersAFinalTranspiler() {
        MethodBase target = typeof(BuilderTranspilerFinalTarget).GetMethod(nameof(BuilderTranspilerFinalTarget.Compute))!;

        using IPatchHandle handle = Patcher.For(target)
            .Transpiler(typeof(BuilderTranspilers), nameof(BuilderTranspilers.FiveToTen), final: true)
            .Apply();

        Assert.True(Assert.IsType<InjectAt.Transpiler>(Assert.Single(backend.Applied).Injection.At).Final);
    }

    [Fact]
    public void Transpiler_Chained_ThroughPatcherFor_AppliesInRegistrationOrder() {
        MethodBase target = typeof(BuilderTranspilerChainTarget).GetMethod(nameof(BuilderTranspilerChainTarget.Compute))!;

        using IPatchHandle handle = Patcher.For(target)
            .Transpiler(typeof(BuilderTranspilers), nameof(BuilderTranspilers.FiveToTen))
            .Transpiler(typeof(BuilderTranspilers), nameof(BuilderTranspilers.TenToTwenty))
            .Apply();

        Assert.Equal(
            [
                typeof(BuilderTranspilers).GetMethod(nameof(BuilderTranspilers.FiveToTen)),
                typeof(BuilderTranspilers).GetMethod(nameof(BuilderTranspilers.TenToTwenty)),
            ],
            backend.InjectionsOn(target).Select(injection => injection.InjectionMethod));
    }
}
