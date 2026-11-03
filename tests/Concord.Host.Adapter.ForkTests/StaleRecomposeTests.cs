using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Detour;
using Xunit;

namespace Concord.Host.Adapter.ForkTests;

public sealed class StaleRecomposeSubject {
    public int Before;
}

public static class StaleRecomposeProbe {
    public static int Counted;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ReadWrapped(StaleRecomposeSubject subject) {
        return subject.Before;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ReadInPlace(StaleRecomposeSubject subject) {
        return subject.Before;
    }

    public static void Count() {
        Counted++;
    }
}

[Collection(ForkFieldInjection.ApplyCollection)]
public sealed class StaleRecomposeTests : IDisposable {
    private const string NoInjection =
        "No field-injection runtime here, so no mod can inject a field and nothing can be reported stale.";

    private static readonly MethodInfo Wrapped =
        typeof(StaleRecomposeProbe).GetMethod(nameof(StaleRecomposeProbe.ReadWrapped))!;

    private static readonly MethodInfo InPlace =
        typeof(StaleRecomposeProbe).GetMethod(nameof(StaleRecomposeProbe.ReadInPlace))!;

    private static readonly MethodInfo Counter =
        typeof(StaleRecomposeProbe).GetMethod(nameof(StaleRecomposeProbe.Count))!;

    private static readonly List<string> Logged = [];

    private static int fields;

    private static bool installed;

    private IDetourHandle? patch;

    private bool written;

    public static bool Unavailable =>
        ForkRuntime.Unavailable || !ForkRuntime.HostRead || ForkFieldInjection.Unavailable
        || ForkInjection.Unavailable;

    public void Dispose() {
        StaleBodyHandler.RewrittenBodyOverride = null;
        StaleBodyHandler.RecomposeOverride = static _ => false;
        patch?.Dispose();
        if (written) {
            ForkRuntime.Restore(InPlace);
            WaylithDetourBackend.Writers.Release(InPlace);
        }

        StaleRecomposeProbe.Counted = 0;
    }

    [Fact(Skip = NoInjection, SkipWhen = nameof(Unavailable))]
    public void AModInjectingAFieldLeavesAWrapperPatchedMethodStillRunningItsPatch() {
        StaleBodyHandler.RecomposeOverride = null;
        Install();

        patch = Patch();
        Reading live = Call(Wrapped, 7);

        patch.Dispose();
        patch = null;
        Reading dead = Call(Wrapped, 7);

        Assert.True(
            live.Patched(7) && dead.Step == 0 && dead.Thrown is null,
            $"a call through the live patch {live} and the same call with the patch disposed {dead}. One step and " +
            "zero steps were wanted. That count is the only thing this fact reads to say whether the patch is " +
            "still running, so two equal readings mean it measures the call rather than the patch and every " +
            "assertion below it is green for free.");

        patch = Patch();
        Reading relive = Call(Wrapped, 7);
        Assert.True(
            relive.Patched(7),
            $"the re-applied patch {relive}, wanted 7 back and one step. The first wrapper was retired on purpose " +
            "to read the dead number, so a target that cannot be patched a second time leaves this fact with " +
            "nothing live to relay out from under.");

        Relaid drive = Relayout("wrapper");
        Assert.True(drive.Applied, drive.Verdict);

        Reading after = Call(Wrapped, 7);

        Assert.True(
            after.Patched(7),
            $"a mod injected a field onto '{typeof(StaleRecomposeSubject).Name}', which '{Wrapped.Name}' reads, " +
            $"and the next call through the patched method {after}. 7 back and one step were wanted: that is what " +
            $"the same call read with the patch live ({live}) and again once it was re-applied ({relive}), " +
            $"against a disposed patch reading {dead}. Concord composes its wrapper as a DynamicMethod, this " +
            "runtime drops every DynamicMethod compiled against the old layout, and the stale-method handler is " +
            "the only channel that gets the wrapper rebuilt. Without it this call throws " +
            "InvalidProgramException, 'compiled against a type layout that has since changed', which is a mod " +
            "taking down every Concord patch on that method by adding a field. The handler logged " +
            $"{Lines().Length} line(s) this run: {Describe(Lines())}.");
    }

    [Fact(Skip = NoInjection, SkipWhen = nameof(Unavailable))]
    public void AModInjectingAFieldLeavesAnInPlaceReplacedBodyStillRunningItsPatch() {
        RefusingBackend fallback = new RefusingBackend();
        new ComposedBodyPublisher(fallback, ForkBodyWriter.Shared)
            .PublishComposed(InPlace, [new Injection(Counter, new InjectAt.Head(), "stale-recompose-in-place", 0)]);
        written = true;

        Assert.True(
            fallback.HandedOff.Count == 0,
            $"the publisher conceded '{InPlace.Name}' to the fallback, so no body was replaced in place and this " +
            "fact would measure the detour path the wrapper fact already covers.");

        Reading live = Call(InPlace, 7);
        Assert.True(
            live.Patched(7),
            $"the in-place replaced body {live}, wanted 7 back and one step, so the patch was never live and the " +
            "relayout below measures nothing.");

        Relaid drive = Relayout("in-place");
        Assert.True(drive.Applied, drive.Verdict);

        Reading after = Call(InPlace, 7);

        ForkRuntime.Restore(InPlace);
        WaylithDetourBackend.Writers.Release(InPlace);
        written = false;
        Reading dead = Call(InPlace, 7);

        Assert.True(
            after.Patched(7) && dead.Step == 0 && dead.Thrown is null,
            $"a body Concord replaced in place {after} after a mod injected a field onto the type it reads, " +
            $"wanted 7 back and one step, and the same call with the declared body restored {dead}, wanted zero " +
            "steps. The pair is the instrument: equal readings mean this fact cannot tell a live patch from a " +
            "dead one. A body in metadata is rejitted by the runtime and never reported to a stale handler, so " +
            "this path has to survive a relayout with no handler involved. A failure here next to a pass in the " +
            "wrapper fact means the two paths swapped and the handler is being credited for the one that never " +
            "needed it.");
    }

    private static void Install() {
        if (installed) {
            return;
        }

        installed = true;
        StaleBodyHandler.Install(Capture);
    }

    private static string[] Lines() {
        lock (Logged) {
            return Logged.ToArray();
        }
    }

    private static string Describe(IReadOnlyList<string> lines) {
        return lines.Count == 0 ? "none" : string.Join(" | ", lines);
    }

    private static void Capture(string line) {
        lock (Logged) {
            Logged.Add(line);
        }
    }

    private static IDetourHandle Patch() {
        return new MonoModDetourBackend()
            .ApplyComposed(Wrapped, [new Injection(Counter, new InjectAt.Head(), "stale-recompose", 0)]);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Reading Call(MethodInfo target, int value) {
        int before = StaleRecomposeProbe.Counted;
        try {
            int returned = Read(target, value);

            return new Reading(returned, StaleRecomposeProbe.Counted - before, null);
        } catch (Exception thrown) {
            return new Reading(0, StaleRecomposeProbe.Counted - before, thrown);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Read(MethodInfo target, int value) {
        return (int)ForkFieldInjection.Unwrapped(() => target.Invoke(null, [NewSubject(value)]))!;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object NewSubject(int value) {
        return new StaleRecomposeSubject { Before = value };
    }

    private static Relaid Relayout(string fact) {
        string name = "ConcordForkTests_StaleRecompose_" + fact + "_" + Interlocked.Increment(ref fields) + "_x";
        Relaid drive = new Relaid(false, "the apply thread never ran");
        Thread worker = new Thread(() => drive = Apply(name));

        worker.Start();
        worker.Join();

        return drive;
    }

    private static Relaid Apply(string fieldName) {
        int epoch = ForkFieldInjection.LayoutEpoch;
        ForkFieldInjection.Batch batch = ForkFieldInjection.NewBatch();
        batch.AddField(typeof(StaleRecomposeSubject), fieldName, typeof(int));
        batch.Commit();

        bool continued = false;
        ForkInjection.QueueForFrameBoundary(batch, () => continued = true);

        List<string> seen;
        try {
            seen = FrameBoundaryApplyTests.DrainPastTransientRefusals(batch);
        } catch (Exception thrown) {
            return new Relaid(
                false,
                $"ApplyPendingAtFrameBoundary threw {thrown.GetType().Name}: {thrown.Message}. The batch read " +
                $"'{batch.Verdict}', so nothing was relaid out and no patch was put under test.");
        }

        bool relaid = continued && batch.Status == "Applied" && epoch != ForkFieldInjection.LayoutEpoch;

        return new Relaid(
            relaid,
            $"injecting '{fieldName}' onto '{typeof(StaleRecomposeSubject).Name}' read '{batch.Verdict}' after " +
            $"[{string.Join(", ", seen)}], the frame-boundary continuation {(continued ? "fired" : "never fired")}, " +
            $"and the layout epoch went {epoch} to {ForkFieldInjection.LayoutEpoch}. An applied batch that fired " +
            "no continuation was drained by another test class, and an unchanged epoch means the type was never " +
            "relaid out, so either way nothing poisoned the patch under test and a green reading below it says " +
            "nothing.");
    }

    private readonly record struct Relaid(bool Applied, string Verdict);

    private readonly record struct Reading(int Returned, int Step, Exception? Thrown) {
        public bool Patched(int wanted) {
            return Thrown is null && Returned == wanted && Step == 1;
        }

        public override string ToString() {
            return Thrown is null
                ? $"returned {Returned} and moved the injection count by {Step}"
                : $"threw {Thrown.GetType().Name} after moving the injection count by {Step}: {Thrown.Message}";
        }
    }
}
