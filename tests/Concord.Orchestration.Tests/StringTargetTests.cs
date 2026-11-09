using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Backend;
using Concord.Emit;
using Xunit;

namespace Concord.Orchestration.Tests;

// Overload disambiguation fixtures.
internal class OverloadDisambiguationTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute(int x) {
        return x + 10;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute(string s) {
        return s.Length;
    }
}

internal static class OverloadDisambiguationInjectionMethod {
    public static void OnComputeInt(ControlHandle<int> ch) {
        ch.ReturnValue = 55;
        ch.Cancel();
    }
}

// Invoke-splice overload disambiguation fixtures.
internal static class OverloadedHelper {
    public static int Process(int x) {
        return x * 2;
    }

    public static int Process(string s) {
        return s.Length * 2;
    }
}

internal static class InvokeDisambiguationCallsite {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Run() {
        return OverloadedHelper.Process(7) + OverloadedHelper.Process("hello");
    }
}

internal static class InvokeDisambiguationInjectionMethod {
    public static void BeforeIntProcess(ControlHandle ch) {
        StringTargetTests.InvokeInjectionMethodHits++;
    }
}

[Patch("Concord.Orchestration.Tests.InternalStringTarget")]
internal static class AmbiguousDisambiguatedDeclaration {
    [Inject(At.Head, "Ambiguous", parameterTypes: [typeof(int)])]
    public static void OnAmbiguousInt(ControlHandle<int> ch) {
        ch.ReturnValue = 99;
        ch.Cancel();
    }
}

// Internal fixture that [Patch] targets by string name.
internal static class InternalStringTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute() {
        return 1;
    }

    public static int Ambiguous(int x) {
        return x;
    }

    public static int Ambiguous(string s) {
        return 0;
    }
}

[Patch("Concord.Orchestration.Tests.InternalStringTarget")]
internal static class StringTargetDeclaration {
    [Inject(At.Head, nameof(InternalStringTarget.Compute))]
    public static void OnCompute(ControlHandle<int> ch) {
        ch.ReturnValue = 77;
        ch.Cancel();
    }
}

[Patch("Concord.Orchestration.Tests.InternalStringTarget")]
internal static class AmbiguousMethodDeclaration {
    [Inject(At.Head, "Ambiguous")]
    public static void OnAmbiguous(ControlHandle<int> ch) { }
}

[Patch("Does.Not.Exist.Type")]
internal static class MissingTypeDeclaration { }

[Patch("Concord.Orchestration.Tests.InternalStringTarget")]
internal static class MissingMethodDeclaration {
    [Inject(At.Head, "NoSuchMethod")]
    public static void OnMissing(ControlHandle ch) { }
}

[Collection(SharedAssemblyApplyCollection.Name)]
public sealed class StringTargetTests : IDisposable {
    internal static int InvokeInjectionMethodHits;

    private readonly RecordingDetourBackend backend = new RecordingDetourBackend();

    public StringTargetTests() {
        PatchBackend.Current = backend;
    }

    public void Dispose() {
        PatchBackend.Reset();
    }

    [Fact]
    public void ScanType_StringTarget_ResolvesTypeAndAppliesPatch() {
        FakePatchApplier patches = new FakePatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        PatchDeclarationScanner.ScanType(typeof(StringTargetDeclaration), patches, props);

        PatchCall call = Assert.Single(patches.Calls);
        Assert.Equal(typeof(InternalStringTarget).GetMethod(nameof(InternalStringTarget.Compute)), call.Target);
    }

    [Fact]
    public void ScanType_StringTargetMissingType_ThrowsConcordDeclarationException() {
        FakePatchApplier patches = new FakePatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        ConcordDeclarationException ex =
            Assert.Throws<ConcordDeclarationException>(() => PatchDeclarationScanner.ScanType(typeof(MissingTypeDeclaration), patches, props));
        Assert.Contains("Does.Not.Exist.Type", ex.Message);
        Assert.Contains("could not be resolved", ex.Message);
    }

    [Fact]
    public void ScanType_MissingMethod_ThrowsConcordDeclarationException() {
        FakePatchApplier patches = new FakePatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        ConcordDeclarationException ex =
            Assert.Throws<ConcordDeclarationException>(() => PatchDeclarationScanner.ScanType(typeof(MissingMethodDeclaration), patches, props));
        Assert.Contains("NoSuchMethod", ex.Message);
        Assert.Contains(typeof(InternalStringTarget).FullName!, ex.Message);
    }

    [Fact]
    public void ScanType_AmbiguousMethod_ThrowsConcordDeclarationExceptionMentioningAmbiguity() {
        FakePatchApplier patches = new FakePatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        ConcordDeclarationException ex =
            Assert.Throws<ConcordDeclarationException>(() => PatchDeclarationScanner.ScanType(typeof(AmbiguousMethodDeclaration), patches, props));
        Assert.Contains("Ambiguous", ex.Message);
        Assert.Contains("ambiguous", ex.Message);
    }

    [Fact]
    public void ScanType_ParameterTypesDisambiguate_ResolvesIntOverload_DoesNotThrow() {
        FakePatchApplier patches = new FakePatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        PatchDeclarationScanner.ScanType(typeof(AmbiguousDisambiguatedDeclaration), patches, props);

        PatchCall call = Assert.Single(patches.Calls);
        MethodInfo? expected = typeof(InternalStringTarget).GetMethod("Ambiguous", [typeof(int)]);
        Assert.Equal(expected, call.Target);
    }

    [Fact]
    public void PatcherForT_ParameterTypes_ResolvesIntOverload() {
        MethodInfo? expected = typeof(OverloadDisambiguationTarget).GetMethod("Compute", [typeof(int)]);
        Assert.NotNull(expected);

        PatchBuilder builder = Patcher.For<OverloadDisambiguationTarget>("Compute", [typeof(int)]);
        MethodInfo injectionMethod = typeof(OverloadDisambiguationInjectionMethod).GetMethod(nameof(OverloadDisambiguationInjectionMethod.OnComputeInt))!;

        IPatchHandle handle = builder.Head(injectionMethod).Apply();
        try {
            Assert.Equal(expected, Assert.Single(backend.Applied).Target);
        } finally {
            handle.Dispose();
        }
    }

    [Fact]
    public void InvokeBuilder_CallSiteParameterTypes_SelectsIntOverloadCallSite() {
        MethodBase target = typeof(InvokeDisambiguationCallsite).GetMethod(nameof(InvokeDisambiguationCallsite.Run))!;
        MethodInfo injectionMethod = typeof(InvokeDisambiguationInjectionMethod).GetMethod(nameof(InvokeDisambiguationInjectionMethod.BeforeIntProcess))!;

        IPatchHandle handle = Patcher
            .For(target)
            .Invoke(typeof(OverloadedHelper), "Process", [typeof(int)], injectionMethod, At.Head)
            .Apply();

        try {
            AppliedInjection applied = Assert.Single(backend.Applied);
            InjectAt.Invoke at = Assert.IsType<InjectAt.Invoke>(applied.Injection.At);

            Assert.Equal(target, applied.Target);
            Assert.Equal(typeof(OverloadedHelper), at.DeclaringType);
            Assert.Equal(nameof(OverloadedHelper.Process), at.Method);
            Assert.Equal([typeof(int)], at.ParameterTypes);
        } finally {
            handle.Dispose();
        }
    }
}
