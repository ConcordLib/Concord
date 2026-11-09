using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Backend;
using Concord.Emit;
using Concord.Orchestration.Tests.RollbackAssembly;
using Xunit;

namespace Concord.Orchestration.Tests;

public class DowngradeGame {
    public virtual int Step(int n) {
        return n + 1;
    }
}

[Patch(typeof(DowngradeGame))]
public abstract class ThrowingTranspilerDeclaration : DowngradeGame {
    [Inject(At.Transpiler, nameof(DowngradeGame.Step))]
    public static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions) {
        throw new InvalidOperationException("author blew up");
    }
}

public class DowngradeGoodTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Step(int n) {
        return n + 1;
    }
}

[Patch(typeof(DowngradeGoodTarget))]
public abstract class DowngradeGoodDeclaration : DowngradeGoodTarget {
    [Inject(At.Tail, nameof(DowngradeGoodTarget.Step))]
    public void AddOneHundred(ControlHandle<int> ch) {
        ch.ReturnValue = ch.ReturnValue + 100;
    }
}

[Collection(SharedAssemblyApplyCollection.Name)]
public sealed class TranspilerDowngradeTests : IDisposable {
    private readonly RecordingDetourBackend backend = new RecordingDetourBackend();

    public TranspilerDowngradeTests() {
        PatchBackend.Current = backend;
    }

    public void Dispose() {
        PatchBackend.Reset();
    }

    [Fact]
    public void ScanType_TranspilerFailure_ThrowsDeclarationExceptionNotEmitException() {
        backend.FailOn = FailEveryTranspiler;
        CollectingPatchApplier applier = new CollectingPatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        ConcordDeclarationException ex = Assert.Throws<ConcordDeclarationException>(
            () => PatchDeclarationScanner.ScanType(typeof(ThrowingTranspilerDeclaration), applier, props));

        Assert.Contains("CONC117", ex.Message);
        Assert.Contains(nameof(ThrowingTranspilerDeclaration), ex.Message);
        Assert.Empty(applier.Handles);
    }

    [Fact]
    public void ScanDeclarations_TranspilerFailure_GoodDeclarationStillApplies() {
        backend.FailOn = FailEveryTranspiler;
        CollectingPatchApplier applier = new CollectingPatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        PatchDeclarationScanner.ScanDeclarations(
            [typeof(DowngradeGoodDeclaration), typeof(ThrowingTranspilerDeclaration)],
            applier,
            props);

        AppliedInjection applied = Assert.Single(backend.Applied);
        Assert.Equal(typeof(DowngradeGoodTarget).GetMethod(nameof(DowngradeGoodTarget.Step)), applied.Target);
        Assert.Equal(typeof(DowngradeGoodDeclaration), applied.Injection.InjectionMethod.DeclaringType);
    }

    [Fact]
    public void ScanType_NonTranspilerEmitFailure_EscapesAsConcordEmitException() {
        backend.FailOn = (_, _) => new ConcordEmitException("CONC012", "cancel without a return value");
        CollectingPatchApplier applier = new CollectingPatchApplier();
        FakeAttachedPropertyRegistry props = new FakeAttachedPropertyRegistry();

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(
            () => PatchDeclarationScanner.ScanType(typeof(RollbackBadDeclaration), applier, props));

        Assert.Equal("CONC012", ex.Code);
        Assert.Empty(applier.Handles);
    }

    private static Exception? FailEveryTranspiler(MethodBase target, Injection injection) {
        return injection.At is InjectAt.Transpiler
            ? new ConcordEmitException("CONC117", "the transpiler threw")
            : null;
    }
}
