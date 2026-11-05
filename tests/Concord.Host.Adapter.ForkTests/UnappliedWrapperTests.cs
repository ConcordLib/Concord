using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Xunit;

namespace Concord.Host.Adapter.ForkTests;

public sealed class UnappliedWrapperLayout {
    public int Before;
}

public static class UnappliedWrapperProbe {
    public static int Heads;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ReadInPlace(UnappliedWrapperLayout subject) {
        return subject.Before;
    }

    public static void CountHead() {
        Heads++;
    }
}

[Collection(ForkFieldInjection.ApplyCollection)]
public sealed class UnappliedWrapperTests : IDisposable {
    private const string NoInjection =
        "No stale-method channel on this runtime, so nothing can report a stale method here.";

    private const string Field = "ConcordForkTests_UnappliedWrapper_x";

    private const int Attempts = 40;

    private const int SettleMs = 25;

    private static readonly MethodInfo Target =
        typeof(UnappliedWrapperProbe).GetMethod(nameof(UnappliedWrapperProbe.ReadInPlace))!;

    private static readonly string[] Transient = ["CompileGateBusy", "GCBusy", "GCNotLatched", "NoGCRegions"];

    private readonly List<MethodBase> seen = [];

    private bool recording = true;

    public UnappliedWrapperTests() {
        ForkInjection.RegisterStaleMethodHandler(Record);
    }

    public static bool Unavailable =>
        ForkRuntime.Unavailable || !ForkRuntime.HostRead || ForkFieldInjection.Unavailable
        || ForkInjection.Unavailable;

    public void Dispose() {
        recording = false;
        ForkRuntime.Restore(Target);
        UnappliedWrapperProbe.Heads = 0;
    }

    [Fact(Skip = NoInjection, SkipWhen = nameof(Unavailable))]
    public void AStaleReportForAPatchedTargetNamesThatTargetAndNotAComposedWrapper() {
        IPatchHandle patch = ForkRuntime.Apply(
            Target,
            typeof(UnappliedWrapperProbe),
            nameof(UnappliedWrapperProbe.CountHead),
            At.Head);

        Assert.True(patch.IsApplied, "the patch reports IsApplied false, so nothing was staged over the target");
        Assert.Equal(7, Invoke(7));
        Assert.Equal(1, UnappliedWrapperProbe.Heads);

        Relayout();

        Assert.Equal(7, Invoke(7));
        Assert.Equal(
            2,
            UnappliedWrapperProbe.Heads);

        MethodBase[] reported = Snapshot();
        MethodBase[] foreign = [.. reported.Where(method => !Names(method))];

        Assert.True(
            foreign.Length == 0,
            "the stale channel reported "
            + Listing(foreign)
            + $" after a relayout of '{nameof(UnappliedWrapperLayout)}'. On the pipeline the composed body is the "
            + "target's own body, so any method the runtime reports stale for a Concord patch is the target "
            + "itself. A name that is not the target is a separate compiled body the stage left behind that "
            + "nothing owns. Everything the channel saw: "
            + Listing(reported));
    }

    private static bool Names(MethodBase method) {
        return method.Name == Target.Name && method.DeclaringType == Target.DeclaringType;
    }

    private static string Listing(IReadOnlyList<MethodBase> methods) {
        return methods.Count == 0
            ? "nothing"
            : string.Join(
                " | ",
                methods.Select(method =>
                    (method.DeclaringType?.FullName ?? "<no declaring type>") + "." + method.Name
                    + " (" + method.GetType().Name + ")"));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Invoke(int value) {
        return (int)ForkFieldInjection.Unwrapped(
            () => Target.Invoke(null, [new UnappliedWrapperLayout { Before = value }]))!;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ForkFieldInjection.Batch Queue(out bool continued) {
        ForkFieldInjection.Batch batch = ForkFieldInjection.NewBatch();
        batch.AddField(typeof(UnappliedWrapperLayout), Field, typeof(int));
        batch.Commit();

        bool applied = false;
        ForkInjection.QueueForFrameBoundary(batch, () => applied = true);

        for (int attempt = 0; attempt < Attempts; attempt++) {
            ForkInjection.ApplyPendingAtFrameBoundary();
            if (batch.Status != "Queued" || !Transient.Contains(batch.LastRefusal)) {
                break;
            }

            Thread.Sleep(SettleMs);
        }

        continued = applied;
        return batch;
    }

    private static void Relayout() {
        int epoch = ForkFieldInjection.LayoutEpoch;

        ForkFieldInjection.Batch batch = Queue(out bool continued);

        Assert.True(batch.Status == "Applied", batch.Verdict);
        Assert.True(continued, "the frame-boundary continuation never ran, so this apply bypassed the queue");
        Assert.NotEqual(epoch, ForkFieldInjection.LayoutEpoch);
    }

    private MethodBase[] Snapshot() {
        lock (seen) {
            return seen.ToArray();
        }
    }

    private bool Record(MethodBase reported) {
        if (recording) {
            lock (seen) {
                seen.Add(reported);
            }
        }

        return false;
    }
}
