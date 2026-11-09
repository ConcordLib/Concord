using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Backend;
using Concord.Emit;
using Xunit;

namespace Concord.Orchestration.Tests;

internal sealed class FluentCtorTarget {
    public int Value;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public FluentCtorTarget(int seed) {
        Value = seed;
    }
}

internal sealed class AttrCtorTarget {
    public int Value;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public AttrCtorTarget() {
        Value = 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public AttrCtorTarget(int seed) {
        Value = seed;
    }
}

internal static class AttrCtorLog {
    public static int HeadFired;

    public static void Reset() {
        HeadFired = 0;
    }
}

[Patch(typeof(AttrCtorTarget))]
internal static class AttrCtorDeclaration {
    [Inject(At.Head)]
    public static void OnConstruct(ControlHandle ch) {
        AttrCtorLog.HeadFired++;
    }
}

internal sealed class OverloadedCtorTarget {
    public string Which;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public OverloadedCtorTarget() {
        Which = "default";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public OverloadedCtorTarget(int x) {
        Which = "int:" + x;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public OverloadedCtorTarget(string s) {
        Which = "string:" + s;
    }
}

internal static class OverloadedCtorIntInjectionMethod {
    public static void OnIntCtor(ControlHandle ch) {
    }
}

internal static class StaticCtorOnlyType {
    static StaticCtorOnlyType() { }
}

internal sealed class WireCtorOverloadTarget {
    public string Which;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public WireCtorOverloadTarget() {
        Which = "default";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public WireCtorOverloadTarget(int x) {
        Which = "int:" + x;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public WireCtorOverloadTarget(string s) {
        Which = "string:" + s;
    }
}

[Patch(typeof(WireCtorOverloadTarget))]
internal static class OverloadedCtorAttrDeclaration {
    [Inject(At.Head, parameterTypes: [typeof(int)])]
    public static void OnIntConstruct(ControlHandle ch) {
    }
}

[Collection(SharedAssemblyApplyCollection.Name)]
public sealed class ConstructorPatchingTests : IDisposable {
    private readonly RecordingDetourBackend backend = new RecordingDetourBackend();

    public ConstructorPatchingTests() {
        PatchBackend.Current = backend;
    }

    public void Dispose() {
        PatchBackend.Reset();
    }

    [Fact]
    public void ScanType_InjectTargetsConstructor_AppliesCtorAsPatchTarget() {
        FakePatchApplier patches = new FakePatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        PatchDeclarationScanner.ScanType(typeof(AttrCtorDeclaration), patches, props);

        PatchCall call = Assert.Single(patches.Calls);
        ConstructorInfo? expectedCtor = typeof(AttrCtorTarget).GetConstructor(Type.EmptyTypes);
        Assert.Equal(expectedCtor, call.Target);
        Assert.IsType<InjectAt.Head>(call.Injection.At);
    }

    [Fact]
    public void ForConstructor_OverloadedCtors_IntOverload_SelectsOnlyIntCtor() {
        MethodInfo injectionMethod = typeof(OverloadedCtorIntInjectionMethod).GetMethod(nameof(OverloadedCtorIntInjectionMethod.OnIntCtor))!;

        IPatchHandle handle = Patcher.ForConstructor<OverloadedCtorTarget>([typeof(int)])
            .Head(injectionMethod)
            .Apply();
        try {
            AppliedInjection applied = Assert.Single(backend.Applied);

            Assert.Equal(typeof(OverloadedCtorTarget).GetConstructor([typeof(int)]), applied.Target);
            Assert.IsType<InjectAt.Head>(applied.Injection.At);
        } finally {
            handle.Dispose();
        }
    }

    [Fact]
    public void ScanType_InjectTargetsConstructor_WithParameterTypes_ResolvesOverloadedCtor() {
        FakePatchApplier patches = new FakePatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        PatchDeclarationScanner.ScanType(typeof(OverloadedCtorAttrDeclaration), patches, props);

        PatchCall call = Assert.Single(patches.Calls);
        ConstructorInfo? expectedCtor = typeof(WireCtorOverloadTarget).GetConstructor([typeof(int)]);
        Assert.Equal(expectedCtor, call.Target);
        Assert.IsType<InjectAt.Head>(call.Injection.At);
    }

    [Fact]
    public void ForConstructor_EmptyParamTypes_SelectsParameterlessCtor() {
        ConstructorInfo? parameterless = typeof(OverloadedCtorTarget).GetConstructor(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            Type.EmptyTypes,
            null);
        Assert.NotNull(parameterless);

        PatchBuilder builder = Patcher.ForConstructor<OverloadedCtorTarget>([]);
        Assert.NotNull(builder);
    }

    [Fact]
    public void ForConstructor_NoMatchingCtor_ThrowsConcordDeclarationException() {
        ConcordDeclarationException ex = Assert.Throws<ConcordDeclarationException>(
            () => Patcher.ForConstructor<FluentCtorTarget>([typeof(double)]));
        Assert.Contains("instance constructor", ex.Message);
        Assert.Contains(typeof(FluentCtorTarget).FullName!, ex.Message);
    }

    [Fact]
    public void ForConstructor_StaticCtorOnlyType_ThrowsConcordDeclarationException() {
        ConcordDeclarationException ex = Assert.Throws<ConcordDeclarationException>(
            () => Patcher.ForConstructor(typeof(StaticCtorOnlyType), []));
        Assert.Contains("instance constructor", ex.Message);
    }
}
