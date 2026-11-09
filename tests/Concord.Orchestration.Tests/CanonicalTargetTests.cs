using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Concord.Backend;
using Concord.Emit;
using Xunit;

namespace Concord.Orchestration.Tests;

[Collection(SharedAssemblyApplyCollection.Name)]
public class CanonicalTargetTests : IDisposable {
    private readonly RecordingDetourBackend backend = new RecordingDetourBackend();

    public CanonicalTargetTests() {
        PatchBackend.Current = backend;
    }

    public void Dispose() {
        PatchBackend.Reset();
    }

    private static async Task<int> AsyncTarget() {
        await Task.Yield();
        return 7;
    }

    private static void Head() {
    }

    private sealed class GenericAsyncContainer<T> {
        public async Task<int> FooAsync() {
            await Task.Yield();
            return 1;
        }
    }

    [Fact]
    public void ConstructedGenericAsyncTarget_RawTargetIsRejected() {
        CollectingPatchApplier applier = new CollectingPatchApplier();
        MethodBase entry = typeof(GenericAsyncContainer<string>).GetMethod(nameof(GenericAsyncContainer<string>.FooAsync))!;
        Injection head = new Injection(typeof(CanonicalTargetTests).GetMethod(nameof(Head), BindingFlags.Static | BindingFlags.NonPublic)!, new InjectAt.Head(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() => applier.ApplyPatch(entry, head));

        Assert.Equal("CONC061", ex.Code);
        Assert.Empty(backend.Applied);
    }

    [Fact]
    public void AsyncTargetWithStateMachineBodyReachesBackendAsMoveNext() {
        Assert.Equal("MoveNext", ApplyAndCaptureTarget(PatchBody.StateMachine).Name);
    }

    // The default. Patching the declared method is what an author who wrote [Inject(At.Return, ...)]
    // and a handle over the Task or IEnumerable means, and it is what Harmony does.
    [Fact]
    public void AsyncTargetWithDeclaredBodyReachesBackendAsTheDeclaredMethod() {
        Assert.Equal(nameof(AsyncTarget), ApplyAndCaptureTarget(PatchBody.Declared).Name);
    }

    private MethodBase ApplyAndCaptureTarget(PatchBody body) {
        CollectingPatchApplier applier = new CollectingPatchApplier();
        MethodBase entry = typeof(CanonicalTargetTests).GetMethod(nameof(AsyncTarget), BindingFlags.Static | BindingFlags.NonPublic)!;
        Injection head = new Injection(
            typeof(CanonicalTargetTests).GetMethod(nameof(Head), BindingFlags.Static | BindingFlags.NonPublic)!,
            new InjectAt.Head(),
            "test",
            0) {
            Body = body,
        };
        applier.ApplyPatch(entry, head);

        return Assert.Single(backend.Applied).Target;
    }
}
