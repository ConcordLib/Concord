using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Concord.Host.Adapter.ForkTests;

public sealed class UnappliedWrapperLayout {
    public int Before;
}

public static class UnappliedWrapperProbe {
    public static int Heads;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ReadLeftAlone(UnappliedWrapperLayout subject) {
        return subject.Before;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ReadInvoked(UnappliedWrapperLayout subject) {
        return subject.Before;
    }

    public static void CountHead() {
        Heads++;
    }
}

[Collection(ForkFieldInjection.ApplyCollection)]
public sealed class UnappliedWrapperTests : IDisposable {
    private const string NoInjection =
        "No stale-method channel on this runtime, so nothing can report an unapplied wrapper here.";

    private const string FirstField = "ConcordForkTests_UnappliedWrapper_first_x";

    private const string SecondField = "ConcordForkTests_UnappliedWrapper_second_x";

    private const int Attempts = 40;

    private const int SettleMs = 25;

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
    }

    [Fact(Skip = NoInjection, SkipWhen = nameof(Unavailable))]
    public void AnUnappliedWrapperReachesTheStaleHandlerOnlyOnceSomethingInvokesIt() {
        ComposeResult leftAlone = ComposeOver(nameof(UnappliedWrapperProbe.ReadLeftAlone));
        ComposeResult invoked = ComposeOver(nameof(UnappliedWrapperProbe.ReadInvoked));
        Assert.Equal(7, Invoke(invoked.Wrapper, 7));

        Relayout(FirstField);

        Assert.True(
            Reported(invoked.Wrapper),
            "a wrapper Concord composed and invoked once, and applied to nothing, was "
            + Verdict(invoked.Wrapper)
            + " This is the control for the reading below: with nothing reported at all, an absent wrapper says "
            + "only that the stale channel never fired, not that the runtime ignores an untouched wrapper.");
        Assert.True(
            !Reported(leftAlone.Wrapper),
            "a wrapper Concord composed and then left alone was "
            + Verdict(leftAlone.Wrapper)
            + " Concord does not track the wrapper ComposeOnce builds on the in-place body path, so a report for "
            + "one can only be answered by MethodIdentity.ResolveOriginal returning null and the handler saying "
            + "false. A reported reading means that honest false is a wrapper the runtime expected someone to "
            + "regenerate, and phase 3 has to decide what to do with it rather than assume the case never arises.");

        Assert.Equal(7, Invoke(leftAlone.Wrapper, 7));
        Relayout(SecondField);

        Assert.True(
            Reported(leftAlone.Wrapper),
            "the same wrapper, after one invoke and still applied to nothing, was "
            + Verdict(leftAlone.Wrapper)
            + " One invoke jits it against the old layout, which is the state a relayout poisons, so the runtime "
            + "reports a wrapper it compiled whether or not anything installed it. An unreported reading would "
            + "mean the runtime only tracks a wrapper something applied, and the handler would never see this "
            + "case at all.");
        GC.KeepAlive(leftAlone.Wrapper);
        GC.KeepAlive(invoked.Wrapper);
    }

    private static ComposeResult ComposeOver(string probeName) {
        MethodBase target = typeof(UnappliedWrapperProbe).GetMethod(probeName)!;
        Injection head = new Injection(
            typeof(UnappliedWrapperProbe).GetMethod(nameof(UnappliedWrapperProbe.CountHead))!,
            new InjectAt.Head(),
            "p2-t5",
            0);

        ComposeResult composed = WrapperComposer.Compose(target, [head]);

        Assert.Empty(composed.Rejected);
        return composed;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Invoke(MethodInfo wrapper, int value) {
        return (int)ForkFieldInjection.Unwrapped(
            () => wrapper.Invoke(null, [new UnappliedWrapperLayout { Before = value }]))!;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ForkFieldInjection.Batch Queue(string fieldName, out bool continued) {
        ForkFieldInjection.Batch batch = ForkFieldInjection.NewBatch();
        batch.AddField(typeof(UnappliedWrapperLayout), fieldName, typeof(int));
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

    private static void Relayout(string fieldName) {
        int epoch = ForkFieldInjection.LayoutEpoch;

        ForkFieldInjection.Batch batch = Queue(fieldName, out bool continued);

        Assert.True(batch.Status == "Applied", batch.Verdict);
        Assert.True(continued, "the frame-boundary continuation never ran, so this apply bypassed the queue");
        Assert.NotEqual(epoch, ForkFieldInjection.LayoutEpoch);
    }

    private bool Reported(MethodInfo wrapper) {
        foreach (MethodBase method in Snapshot()) {
            if (ReferenceEquals(method, wrapper) || method == wrapper || method.Name == wrapper.Name) {
                return true;
            }
        }

        return false;
    }

    private string Verdict(MethodInfo wrapper) {
        MethodBase[] reported = Snapshot();
        string names = reported.Length == 0
            ? "none"
            : string.Join(
                " | ",
                reported.Select(method =>
                    (method.DeclaringType?.FullName ?? "<no declaring type>") + "." + method.Name
                    + " (" + method.GetType().Name + ")"));

        return $"{(Reported(wrapper) ? "reported" : "not reported")} to the stale handler. Wrapper "
            + $"'{wrapper.Name}' is a {wrapper.GetType().Name}, CaptureComposedBody is "
            + $"{WrapperComposer.CaptureComposedBody}, and the handler has seen {reported.Length} method(s) "
            + $"across every batch these applies drained: {names}.";
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
