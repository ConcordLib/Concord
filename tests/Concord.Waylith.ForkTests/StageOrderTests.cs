using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Concord.Waylith.ForkTests;

public static class StageOrderVictims {
    public static int Heads;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Above() {
        return 51;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Below() {
        return 52;
    }

    public static void CountHead() {
        Heads++;
    }
}

[Collection(ForkFieldInjection.ApplyCollection)]
public sealed class StageOrderTests : IDisposable {
    private const string NoPipeline = "No transformer pipeline here, so stage order cannot be measured.";

    private readonly List<StandInTransformer> registered = [];

    private readonly List<MethodBase> patched = [];

    public static bool Unavailable => ForkRuntime.Unavailable;

    public void Dispose() {
        foreach (MethodBase target in patched) {
            ForkRuntime.Restore(target);
        }

        patched.Clear();
        foreach (StandInTransformer standIn in registered) {
            standIn.Unregister();
        }

        registered.Clear();
        StageOrderVictims.Heads = 0;
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void AStandInAbovePriorityZeroReceivesConcordsComposedBodyAsItsInput() {
        MethodInfo target = typeof(StageOrderVictims).GetMethod(nameof(StageOrderVictims.Above))!;
        byte[] declared = target.GetMethodBody()!.GetILAsByteArray()!;
        StandInTransformer standIn = Registered(0, static _ => null);

        IPatchHandle patch = Patch(target);
        Assert.True(patch.IsApplied, "the handle reports IsApplied false, so nothing was staged to feed the stand-in");

        Exception? failure = standIn.Rebuild(target);

        Assert.Null(failure);
        Assert.True(
            standIn.SeenIl.Count == 1,
            $"the stand-in ran {standIn.SeenIl.Count} time(s) and has to run once, or the IL below is not the " +
            "input the pipeline handed a stage above Concord's.");
        Assert.True(
            !standIn.SeenIl[0].SequenceEqual(declared),
            $"a stand-in at priority 0 received {standIn.SeenIl[0].Length} bytes of IL identical to the " +
            $"{declared.Length} bytes the method declares. Concord's stage sits at -1000, so a stage above it " +
            "has to receive Concord's composed body as its input. Receiving the declared body means the two " +
            "stages are not chained and whichever runs last wins.");
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void AStandInBelowConcordsPriorityFeedsConcordItsInput() {
        MethodInfo target = typeof(StageOrderVictims).GetMethod(nameof(StageOrderVictims.Below))!;
        StandInTransformer standIn = Registered(-2000, static _ => ForkRuntime.Returning(99));

        Exception? failure = standIn.Rebuild(target);
        Assert.Null(failure);
        Assert.Equal(99, StageOrderVictims.Below());

        IPatchHandle patch = Patch(target);

        Assert.True(patch.IsApplied, "the handle reports IsApplied false, so Concord staged nothing over the rewrite");
        Assert.Equal(
            99,
            StageOrderVictims.Below());
        Assert.True(
            StageOrderVictims.Heads == 1,
            $"the injection ran {StageOrderVictims.Heads} time(s) over the stand-in's rewrite and has to run " +
            "once. Concord's stage sits at -1000, so a stage at -2000 runs first and its output is what Concord " +
            "composes against. A rewrite that loses the injection means Concord read the declared body instead " +
            "of the input it was handed, and a return value that is not 99 means Concord threw the rewrite away.");
    }

    private StandInTransformer Registered(int priority, Func<byte[], byte[]?> rewrite) {
        StandInTransformer standIn = new StandInTransformer(priority, rewrite);
        standIn.Register();
        registered.Add(standIn);
        return standIn;
    }

    private IPatchHandle Patch(MethodBase target) {
        IPatchHandle handle = ForkRuntime.Apply(
            target,
            typeof(StageOrderVictims),
            nameof(StageOrderVictims.CountHead),
            At.Head);
        patched.Add(target);
        return handle;
    }
}
