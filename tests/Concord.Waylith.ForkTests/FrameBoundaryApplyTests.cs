using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Concord.Waylith.ForkTests;

public sealed class BoundaryTarget {
    public int Before;
}

public sealed class BoundaryPairTarget {
    public int Before;
}

public sealed class BoundaryFirstConsumerTarget {
    public int Before;
}

public sealed class BoundarySecondConsumerTarget {
    public int Before;
}

public sealed class BoundaryContrastTarget {
    public int Before;
}

public sealed class BoundaryDependentTarget {
    public int Before;
}

public static class BoundaryDependentProbe {
    private static int fired;

    public static int Fired => Volatile.Read(ref fired);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string Descend(BoundaryDependentTarget subject, int depth) {
        if (depth > 0) {
            return Descend(subject, depth - 1) + (subject.Before == int.MinValue ? "!" : string.Empty);
        }

        ForkFieldInjection.Batch batch = ForkFieldInjection.NewBatch();
        batch.AddField(
            typeof(BoundaryDependentTarget),
            FrameBoundaryApplyTests.Named(nameof(BoundaryDependentTarget)),
            typeof(int));

        ForkInjection.QueueForFrameBoundary(batch, () => Interlocked.Increment(ref fired));
        List<string> seen = FrameBoundaryApplyTests.DrainPastTransientRefusals(batch);

        return batch.Status + " " + batch.LastRefusal + ": " + (batch.PendingReason ?? "no reason given")
            + " after [" + string.Join(", ", seen) + "]"
            + (subject.Before == int.MinValue ? "!" : string.Empty);
    }
}

[Collection(ForkFieldInjection.ApplyCollection)]
public sealed class FrameBoundaryApplyTests {
    internal const int Attempts = 40;

    internal const int SettleMs = 25;

    private const string NoInjection =
        "No field-injection runtime here, so nothing can be queued for a frame boundary.";

    private const int Depth = 8;

    private const BindingFlags AnyField = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    internal static readonly string[] Transient = ["CompileGateBusy", "GCBusy", "GCNotLatched", "NoGCRegions"];

    public static bool Unavailable =>
        ForkRuntime.Unavailable || ForkFieldInjection.Unavailable || ForkInjection.Unavailable;

    public static string Named(string subject) {
        return "ConcordForkTests_Boundary_" + subject + "_x";
    }

    internal static List<string> DrainPastTransientRefusals(ForkFieldInjection.Batch batch) {
        List<string> seen = [];

        for (int attempt = 0; attempt < Attempts; attempt++) {
            ForkInjection.ApplyPendingAtFrameBoundary();
            seen.Add(batch.LastRefusal);

            if (batch.Status != "Queued" || !Transient.Contains(batch.LastRefusal)) {
                return seen;
            }

            Thread.Sleep(SettleMs);
        }

        return seen;
    }

    [Fact(Skip = NoInjection, SkipWhen = nameof(Unavailable))]
    public void AQueuedBatchAppliesAtTheFrameBoundaryAndThenRunsItsContinuation() {
        string name = Named(nameof(BoundaryTarget));
        ForkFieldInjection.Batch batch = ForkFieldInjection.NewBatch();
        ForkFieldInjection.Injected injected = batch.AddField(typeof(BoundaryTarget), name, typeof(int));
        int fired = 0;
        bool fieldWasRealWhenItFired = false;
        bool reflectionSawItWhenItFired = false;

        ForkInjection.QueueForFrameBoundary(batch, () => {
            fired++;
            fieldWasRealWhenItFired = injected.Field is not null;
            reflectionSawItWhenItFired = typeof(BoundaryTarget).GetField(name, AnyField) is not null;
        });

        Assert.True(
            fired == 0,
            $"the continuation ran {fired} times from the queue call alone, so QueueForFrameBoundary applied the " +
            "batch itself. Concord queues on a worker thread and applies on the main thread's frame boundary, so an " +
            "apply inside the queue call relays a type from whichever thread got there first.");

        DrainPastTransientRefusals(batch);

        Assert.True(batch.Verdict == "Applied", batch.Verdict);
        Assert.True(
            fired == 1,
            $"ApplyPendingAtFrameBoundary applied the batch but the continuation ran {fired} times, wanted exactly " +
            "once. ApplyPendingAtFrameBoundary returns void, so the continuation is the only signal Concord gets " +
            "that its own batch landed; a zero means Concord never learns, and a two means it rebuilds twice.");
        Assert.True(
            fieldWasRealWhenItFired && reflectionSawItWhenItFired,
            $"when the continuation ran, InjectedField.Field was " +
            $"{(fieldWasRealWhenItFired ? "set" : "still null")} and reflection " +
            $"{(reflectionSawItWhenItFired ? "saw" : "did not see")} '{name}' on the type, wanted both real. The " +
            "continuation is where Concord recomposes, so a field that is not real yet means it recomposes against " +
            "the old layout and has to be told again.");
    }

    [Fact(Skip = NoInjection, SkipWhen = nameof(Unavailable))]
    public void TheContinuationSeesEveryFieldOfTheBatchAlreadyAssigned() {
        ForkFieldInjection.Batch batch = ForkFieldInjection.NewBatch();
        ForkFieldInjection.Injected first = batch.AddField(
            typeof(BoundaryPairTarget),
            Named(nameof(BoundaryPairTarget) + "First"),
            typeof(int));
        ForkFieldInjection.Injected second = batch.AddField(
            typeof(BoundaryPairTarget),
            Named(nameof(BoundaryPairTarget) + "Second"),
            typeof(string));
        string statusWhenItFired = "the continuation never ran";
        int unassignedWhenItFired = -1;

        ForkInjection.QueueForFrameBoundary(batch, () => {
            statusWhenItFired = batch.Status;
            unassignedWhenItFired = (first.Field is null ? 1 : 0) + (second.Field is null ? 1 : 0);
        });

        DrainPastTransientRefusals(batch);

        Assert.True(batch.Verdict == "Applied", batch.Verdict);
        Assert.True(
            statusWhenItFired == "Applied" && unassignedWhenItFired == 0,
            $"the continuation saw Status '{statusWhenItFired}' with {unassignedWhenItFired} of 2 fields still " +
            "unassigned, wanted 'Applied' with zero. The runtime sets Status to Applied before the loop that " +
            "assigns each InjectedField.Field, so that window exists in the apply itself; this measures that no " +
            "consumer can land in it, because the continuation runs after ApplyPending returns. A non-zero count " +
            "here would mean phase 3 has to wait on the fields instead of on the continuation, and a reader that " +
            "trusted the status would hand a null FieldInfo to a body it is composing.");
    }

    [Fact(Skip = NoInjection, SkipWhen = nameof(Unavailable))]
    public void OneFrameBoundaryDrainsTwoConsumersAndRunsBothContinuations() {
        int firstFired = 0;
        int secondFired = 0;
        ForkFieldInjection.Batch first = ForkFieldInjection.NewBatch();
        first.AddField(
            typeof(BoundaryFirstConsumerTarget),
            Named(nameof(BoundaryFirstConsumerTarget)),
            typeof(int));
        ForkFieldInjection.Batch second = ForkFieldInjection.NewBatch();
        second.AddField(
            typeof(BoundarySecondConsumerTarget),
            Named(nameof(BoundarySecondConsumerTarget)),
            typeof(int));

        ForkInjection.QueueForFrameBoundary(first, () => firstFired++);
        ForkInjection.QueueForFrameBoundary(second, () => secondFired++);
        DrainPastTransientRefusals(first);
        DrainPastTransientRefusals(second);

        Assert.True(first.Verdict == "Applied" && second.Verdict == "Applied", first.Verdict + " / " + second.Verdict);
        Assert.True(
            firstFired == 1 && secondFired == 1,
            $"both batches applied but the continuations ran {firstFired} and {secondFired} times, wanted one each. " +
            "The frame boundary is shared: Concord and every other mod queue into the same runtime list, and one " +
            "ApplyPendingAtFrameBoundary drains all of it. A zero on either side is the cost this path exists to " +
            "avoid, because the mod whose continuation was skipped never rebuilds the patches the relayout broke " +
            "and its methods keep throwing with nothing in the log naming Concord's apply as the cause.");
    }

    [Fact(Skip = NoInjection, SkipWhen = nameof(Unavailable))]
    public void ApplyPendingAppliesAnotherConsumersBatchWithoutRunningItsContinuation() {
        int fired = 0;
        ForkFieldInjection.Batch batch = ForkFieldInjection.NewBatch();
        batch.AddField(typeof(BoundaryContrastTarget), Named(nameof(BoundaryContrastTarget)), typeof(int));

        ForkInjection.QueueForFrameBoundary(batch, () => fired++);

        for (int attempt = 0; attempt < Attempts; attempt++) {
            ForkFieldInjection.ApplyPending();

            if (batch.Status != "Queued" || !Transient.Contains(batch.LastRefusal)) {
                break;
            }

            Thread.Sleep(SettleMs);
        }

        Assert.True(batch.Verdict == "Applied", batch.Verdict);
        Assert.True(
            fired == 0,
            $"this fact deliberately contrasts the two apply paths: it calls the banned FieldInjection.ApplyPending " +
            $"on a batch queued through Injection.QueueForFrameBoundary, and the continuation ran {fired} times, " +
            "wanted zero. Zero is what makes the ban load-bearing. ApplyPending dequeues every queued batch and " +
            "knows nothing about the continuations Injection holds, so one mod calling it swallows the frame " +
            "boundary for every other mod. A non-zero reading here would mean the ban is cosmetic and Concord could " +
            "apply directly, which no other fact in this build checks.");
    }

    [Fact(Skip = NoInjection, SkipWhen = nameof(Unavailable))]
    public void AFrameBelowTheFrameBoundaryApplyRefusesTheRelayoutAndWithholdsTheContinuation() {
        BoundaryDependentTarget subject = NewDependent(5);
        string verdict = "never ran";
        Thread worker = new Thread(() => verdict = BoundaryDependentProbe.Descend(subject, Depth));

        worker.Start();
        worker.Join();

        Assert.StartsWith("Queued DependentFrames: ", verdict, StringComparison.Ordinal);
        Assert.Contains(nameof(BoundaryDependentProbe.Descend), verdict, StringComparison.Ordinal);
        Assert.True(
            BoundaryDependentProbe.Fired == 0,
            $"the batch is still Queued on DependentFrames but its continuation ran " +
            $"{BoundaryDependentProbe.Fired} times, wanted zero. A continuation that fires on a refused batch tells " +
            $"Concord to recompose against fields that do not exist yet. The verdict '{verdict}' lists every refusal " +
            "this fact read on the way, one per ApplyPendingAtFrameBoundary call: a CompileGateBusy ahead of the " +
            "DependentFrames is the masking the design calls out, and it is why this loop retried past the transient " +
            "reasons instead of branching on the first one.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static BoundaryDependentTarget NewDependent(int value) {
        return new BoundaryDependentTarget { Before = value };
    }
}
