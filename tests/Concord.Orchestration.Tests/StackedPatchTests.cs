using System.Reflection;
using System.Runtime.CompilerServices;
using Concord;
using Concord.Backend;
using Xunit;

namespace Concord.Orchestration.Tests;

public static class StackedTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Value() {
        return 0;
    }
}

public static class StackedFirstInjection {
    public static void Add10(ControlHandle<int> ch) {
        ch.ReturnValue = ch.ReturnValue + 10;
    }
}

public static class StackedSecondInjection {
    public static void Add100(ControlHandle<int> ch) {
        ch.ReturnValue = ch.ReturnValue + 100;
    }
}

public static class PriorityTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Value() {
        return 5;
    }
}

[Patch(typeof(PriorityTarget))]
public static class PriorityFirstLow {
    [Inject(At.Tail, nameof(PriorityTarget.Value), Priority = 1)]
    public static void AddOne(ControlHandle<int> ch) {
        ch.ReturnValue = ch.ReturnValue + 1;
    }
}

[Patch(typeof(PriorityTarget))]
public static class PrioritySecondHigh {
    [Inject(At.Tail, nameof(PriorityTarget.Value), Priority = 2)]
    public static void MultiplyTwo(ControlHandle<int> ch) {
        ch.ReturnValue = ch.ReturnValue * 2;
    }
}

[Collection(SharedAssemblyApplyCollection.Name)]
public sealed class StackedPatchTests : IDisposable {
    private readonly RecordingDetourBackend backend = new RecordingDetourBackend();

    public StackedPatchTests() {
        PatchBackend.Current = backend;
    }

    public void Dispose() {
        PatchBackend.Reset();
    }

    [Fact]
    public void TwoInjectionsOnOneTarget_BothReachTheSameTarget() {
        MethodBase target = typeof(StackedTarget).GetMethod(nameof(StackedTarget.Value))!;
        MethodBase first = typeof(StackedFirstInjection).GetMethod(nameof(StackedFirstInjection.Add10))!;
        MethodBase second = typeof(StackedSecondInjection).GetMethod(nameof(StackedSecondInjection.Add100))!;

        IPatchHandle h1 = Patcher.Patch(target, first, At.Tail);
        IPatchHandle h2 = Patcher.Patch(target, second, At.Tail);
        try {
            Assert.Equal(
                [typeof(StackedFirstInjection).FullName!, typeof(StackedSecondInjection).FullName!],
                backend.OwnersOf(target));
            Assert.Equal(1, backend.PatchedTargetCount);
        } finally {
            h2.Dispose();
            h1.Dispose();
        }

        Assert.Empty(backend.OwnersOf(target));
    }

    [Fact]
    public void OutOfOrderDispose_LeavesTheSurvivingInjectionOnTheTarget() {
        MethodBase target = typeof(StackedTarget).GetMethod(nameof(StackedTarget.Value))!;
        MethodBase first = typeof(StackedFirstInjection).GetMethod(nameof(StackedFirstInjection.Add10))!;
        MethodBase second = typeof(StackedSecondInjection).GetMethod(nameof(StackedSecondInjection.Add100))!;

        IPatchHandle h1 = Patcher.Patch(target, first, At.Tail);
        IPatchHandle h2 = Patcher.Patch(target, second, At.Tail);

        h1.Dispose();
        Assert.Equal([typeof(StackedSecondInjection).FullName!], backend.OwnersOf(target));

        h2.Dispose();
        Assert.Empty(backend.OwnersOf(target));
    }

    [Fact]
    public void ScanType_CarriesTheDeclaredPriorityToTheBackend() {
        FakePatchApplier applier = new FakePatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        PatchDeclarationScanner.ScanType(typeof(PriorityFirstLow), applier, props);
        PatchDeclarationScanner.ScanType(typeof(PrioritySecondHigh), applier, props);

        Assert.Equal([1, 2], applier.Calls.Select(call => call.Injection.Priority));
    }
}
