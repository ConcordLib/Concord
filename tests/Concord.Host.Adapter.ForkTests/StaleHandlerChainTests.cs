using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace Concord.Host.Adapter.ForkTests;

public sealed class AdditiveChainTarget {
    public int Before;
}

public sealed class PropertyFirstChainTarget {
    public int Before;
}

public sealed class FirstTrueChainTarget {
    public int Before;
}

public sealed class ThrowingChainTarget {
    public int Before;
}

[Collection(ForkFieldInjection.ApplyCollection)]
public sealed class StaleHandlerChainTests : IDisposable {
    private const string NoInjection = "No field-injection runtime here, so no stale-method handler can be registered.";

    private const int Attempts = 40;

    private const int SettleMs = 25;

    private static readonly string[] Transient = ["CompileGateBusy", "GCBusy", "GCNotLatched", "NoGCRegions"];

    private readonly Delegate? regenerateOnEntry;

    private bool wroteRegenerate;

    public StaleHandlerChainTests() {
        regenerateOnEntry = Unavailable ? null : ForkInjection.HarmonyRegenerate;
    }

    public static bool Unavailable => ForkRuntime.Unavailable || ForkInjection.Unavailable;

    public void Dispose() {
        if (wroteRegenerate) {
            ForkInjection.HarmonyRegenerate = regenerateOnEntry;
        }
    }

    [Fact(Skip = NoInjection, SkipWhen = nameof(Unavailable))]
    public void BothRegisteredStaleHandlersAreCalled() {
        string probe = Named("Additive");
        List<string> ran = [];
        ForkInjection.RegisterStaleMethodHandler(handed => Record(handed, probe, ran, "first", false));
        ForkInjection.RegisterStaleMethodHandler(handed => Record(handed, probe, ran, "second", false));
        Delegate reader = Compile(typeof(AdditiveChainTarget), probe);

        (bool Applied, string Verdict, Exception? Thrown) drive = Inject(typeof(AdditiveChainTarget), probe);

        GC.KeepAlive(reader);
        AssertChainRan(drive, ran, probe);
        Assert.True(
            ran is ["first", "second"],
            $"the chain ran [{string.Join(", ", ran)}] over two handlers that both returned false. Both were " +
            "wanted, in registration order, because RegisterStaleMethodHandler is additive. If only one ran, a " +
            "second mod registering a handler would silently cancel the first, and whichever mod lost would have " +
            "its patches left poisoned after a field injection.");
    }

    [Fact(Skip = NoInjection, SkipWhen = nameof(Unavailable))]
    public void TheHarmonyRegeneratePropertyRunsBeforeAnyRegisteredHandler() {
        string probe = Named("PropertyFirst");
        List<string> ran = [];
        ForkInjection.RegisterStaleMethodHandler(handed => Record(handed, probe, ran, "registered", false));
        Delegate reader = Compile(typeof(PropertyFirstChainTarget), probe);
        wroteRegenerate = true;
        ForkInjection.HarmonyRegenerate =
            ForkInjection.NewHandler(handed => Record(handed, probe, ran, "property", false));

        (bool Applied, string Verdict, Exception? Thrown) drive;
        try {
            drive = Inject(typeof(PropertyFirstChainTarget), probe);
        } finally {
            ForkInjection.HarmonyRegenerate = regenerateOnEntry;
        }

        GC.KeepAlive(reader);
        AssertChainRan(drive, ran, probe);
        Assert.True(
            ran is ["property", "registered"],
            $"the chain ran [{string.Join(", ", ran)}]. The HarmonyRegenerate property was wanted first and the " +
            "registered handler second. The property is a single slot any later writer overwrites, so Concord " +
            "registers instead; if the property ran last, a mod that set it could never answer first and the " +
            "ordering Concord relies on would be the other way round.");
    }

    [Fact(Skip = NoInjection, SkipWhen = nameof(Unavailable))]
    public void AHandlerAfterOneThatAnsweredTrueNeverRuns() {
        string probe = Named("FirstTrue");
        List<string> ran = [];
        ForkInjection.RegisterStaleMethodHandler(handed => Record(handed, probe, ran, "answered", true));
        ForkInjection.RegisterStaleMethodHandler(handed => Record(handed, probe, ran, "after", false));
        Delegate reader = Compile(typeof(FirstTrueChainTarget), probe);

        (bool Applied, string Verdict, Exception? Thrown) drive = Inject(typeof(FirstTrueChainTarget), probe);

        GC.KeepAlive(reader);
        AssertChainRan(drive, ran, probe);
        Assert.True(
            ran is ["answered"],
            $"the chain ran [{string.Join(", ", ran)}] over a handler that returned true followed by one that " +
            "returned false. Only the first was wanted, because the chain stops on the first true. If the rest " +
            "still ran, every handler in the process would be asked to regenerate a method another handler had " +
            "already rebuilt, and the second rebuild would throw away the first mod's patch.");
    }

    [Fact(Skip = NoInjection, SkipWhen = nameof(Unavailable))]
    public void AHandlerThatThrowsKillsTheRestOfTheChain() {
        string probe = Named("Throwing");
        List<string> ran = [];
        ForkInjection.RegisterStaleMethodHandler(handed => Record(handed, probe, ran, "before", false));
        ForkInjection.RegisterStaleMethodHandler(handed => Throw(handed, probe, ran));
        ForkInjection.RegisterStaleMethodHandler(handed => Record(handed, probe, ran, "after", false));
        Delegate reader = Compile(typeof(ThrowingChainTarget), probe);

        (bool Applied, string Verdict, Exception? Thrown) drive = Inject(typeof(ThrowingChainTarget), probe);

        GC.KeepAlive(reader);
        AssertChainRan(drive, ran, probe);
        Assert.True(
            ran is ["before", "thrower"],
            $"the chain ran [{string.Join(", ", ran)}] over a handler, then one that threw, then a third. The " +
            "first two were wanted and the third was not, because one try/catch wraps the whole loop, so a throw " +
            "anywhere in it ends the loop. This is why Concord's own handler must catch everything itself: a mod " +
            "whose handler throws would otherwise leave every Concord patch on that method poisoned, and the " +
            "player sees the throw from the patched method, not from the mod that caused it.");
    }

    private static string Named(string subject) {
        return "ConcordStaleChain_" + subject + "_x";
    }

    private static bool Record(MethodBase handed, string probe, List<string> ran, string label, bool verdict) {
        if (handed.Name != probe) {
            return false;
        }

        lock (ran) {
            ran.Add(label);
        }

        return verdict;
    }

    private static bool Throw(MethodBase handed, string probe, List<string> ran) {
        if (handed.Name != probe) {
            return false;
        }

        lock (ran) {
            ran.Add("thrower");
        }

        throw new InvalidOperationException(
            $"'{probe}' is the method this fact asked the runtime to report stale, and this handler throws on " +
            "purpose to measure what the rest of the chain does.");
    }

    private static Delegate Compile(Type declaringType, string probe) {
        DynamicMethod method = new DynamicMethod(
            probe,
            typeof(int),
            [declaringType],
            declaringType.Module,
            skipVisibility: true);

        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, declaringType.GetField("Before")!);
        il.Emit(OpCodes.Ret);

        Delegate reader = method.CreateDelegate(typeof(Func<,>).MakeGenericType(declaringType, typeof(int)));
        object subject = Activator.CreateInstance(declaringType)!;
        Assert.Equal(0, reader.DynamicInvoke(subject));
        return reader;
    }

    private static (bool Applied, string Verdict, Exception? Thrown) Inject(Type declaringType, string probe) {
        (bool Applied, string Verdict, Exception? Thrown) drive = (false, "the apply thread never ran", null);
        Thread worker = new Thread(() => drive = Apply(declaringType, probe));

        worker.Start();
        worker.Join();

        return drive;
    }

    private static (bool Applied, string Verdict, Exception? Thrown) Apply(Type declaringType, string probe) {
        ForkFieldInjection.Batch batch = ForkFieldInjection.NewBatch();
        batch.AddField(declaringType, probe, typeof(int));
        bool applied = false;
        ForkInjection.QueueForFrameBoundary(batch, () => applied = true);

        for (int attempt = 0; attempt < Attempts; attempt++) {
            try {
                ForkInjection.ApplyPendingAtFrameBoundary();
            } catch (Exception thrown) {
                return (applied, batch.Verdict, thrown);
            }

            if (applied || !Transient.Contains(batch.LastRefusal)) {
                break;
            }

            Thread.Sleep(SettleMs);
        }

        return (applied, batch.Verdict, null);
    }

    private static void AssertChainRan(
        (bool Applied, string Verdict, Exception? Thrown) drive,
        List<string> ran,
        string probe) {
        Assert.True(
            drive.Thrown is null,
            $"ApplyPendingAtFrameBoundary threw {drive.Thrown?.GetType().Name}: {drive.Thrown?.Message}. It was " +
            "wanted to return normally, because the runtime catches a handler's throw inside " +
            "RunStaleMethodHandlers. A throw escaping to the caller would mean one mod's bad handler aborts the " +
            "frame-boundary apply for every other mod in the save.");
        Assert.True(
            drive.Applied,
            $"the batch read '{drive.Verdict}' and the onApplied continuation never fired, so Injection.ApplyNow " +
            "did not apply this batch and the handler chain was never entered. The continuation and the " +
            "stale-method loop are in the same ApplyNow call, so a batch that reads Applied without firing it " +
            "was taken by a parallel test class calling FieldInjection.ApplyPending directly. Nothing below this " +
            "line measured the chain.");
        Assert.True(
            ran.Count > 0,
            $"the batch applied and the continuation fired, but no handler was handed '{probe}', so the " +
            "DynamicMethod reading the relaid-out type was never reported stale. A stale report is the runtime's " +
            "only channel out, so with none of them Concord would never hear that its wrappers are poisoned and " +
            "the player's first sign would be a patched method throwing.");
    }
}
