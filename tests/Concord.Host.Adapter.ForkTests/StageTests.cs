using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Concord.Detour;
using Concord.Orchestration;
using Xunit;

namespace Concord.Host.Adapter.ForkTests;

public enum StageWeather {
    Clear = 0,
    Rain = 1,
}

[Patch]
public abstract class StageWeatherExtension : ExtendedEnum<StageWeather> {
    public static StageWeather Frozen;
}

public sealed class StageFieldSubject {
    public int Before;
}

public static class StageVictims {
    public static readonly List<string> Ran = [];

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int One() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Two() {
        return 2;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Three() {
        return 3;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Rejected() {
        return 4;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int RejectedAmongOwners() {
        return 5;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ReadField(StageFieldSubject subject) {
        return subject.Before;
    }

    public static void First() {
        Ran.Add("first");
    }

    public static void Second() {
        Ran.Add("second");
    }
}

[Collection(ForkFieldInjection.ApplyCollection)]
public sealed class StageTests : IDisposable {
    private const string NoPipeline = "No transformer pipeline here, so nothing can be staged over a method body.";

    private const string Field = "ConcordForkTests_Stage_x";

    private const int Attempts = 40;

    private const int SettleMs = 25;

    private static readonly string[] Transient = ["CompileGateBusy", "GCBusy", "GCNotLatched", "NoGCRegions"];

    private readonly Action<string> logged = WaylithDetourBackend.Log;

    private readonly List<string> lines = [];

    private readonly List<MethodBase> patched = [];

    private readonly List<MethodBase> registered = [];

    public StageTests() {
        WaylithDetourBackend.Log = line => lines.Add(line);
    }

    public static bool Unavailable => ForkRuntime.Unavailable;

    public void Dispose() {
        WaylithDetourBackend.Log = logged;
        foreach (MethodBase target in patched) {
            ForkRuntime.Restore(target);
        }

        foreach (MethodBase target in registered) {
            FrameAttribution.Forget(target);
        }

        patched.Clear();
        StageVictims.Ran.Clear();
    }

    [Fact]
    public void TheRuntimeUnderTheseFactsCarriesTheTransformerPipeline() {
        Assert.True(!ForkRuntime.Unavailable, ForkRuntime.GapMarker + " " + ForkRuntime.Explain);
        Assert.True(
            AdapterEntryPoint.Current is { Waylith: true },
            ForkRuntime.GapMarker + " the pipeline is here but the install did not pick the waylith backend, so " +
            "every Patcher.Patch in this project goes somewhere else and no fact below measures the stage. " +
            "The install reported: " + (AdapterEntryPoint.Current?.Message ?? "nothing, it never ran"));
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void AVictimWithOneInjectionRunsItAfterTheComposedBodyIsStaged() {
        MethodInfo target = Victim(nameof(StageVictims.One));

        IPatchHandle patch = Patch(target, nameof(StageVictims.First), At.Head);

        Assert.True(patch.IsApplied, "the handle reports IsApplied false, so nothing was staged: " + Lines());
        Assert.Equal(1, StageVictims.One());
        Assert.Equal(
            new[] { "first" },
            StageVictims.Ran.ToArray());

        string[] reports = [.. lines.Where(line => line.StartsWith("[Concord.Waylith]", StringComparison.Ordinal))];
        Assert.True(
            reports.Length == 0,
            $"staging an ordinary method target wrote {reports.Length} '[Concord.Waylith]' lines and a clean " +
            "compose has to write none. " + Lines());
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void TwoOwnersOnOneMethodBothRunAndTheHeadRunsBeforeTheTail() {
        MethodInfo target = Victim(nameof(StageVictims.Two));

        IPatchHandle head = PatchAs(target, nameof(StageVictims.First), new InjectAt.Head(), "owner-a");
        IPatchHandle tail = PatchAs(target, nameof(StageVictims.Second), new InjectAt.Tail(), "owner-b");

        Assert.True(
            head.IsApplied && tail.IsApplied,
            $"head applied={head.IsApplied} and tail applied={tail.IsApplied}. The second publish has to carry " +
            "both owners' injections, so one handle reading false means the republish dropped the other owner. " +
            Lines());
        Assert.Equal(2, StageVictims.Two());
        Assert.Equal(
            new[] { "first", "second" },
            StageVictims.Ran.ToArray());
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void DisposingTheLastHandlePutsTheVictimBackOnItsDefaultBody() {
        MethodInfo target = Victim(nameof(StageVictims.Three));

        IPatchHandle patch = Patch(target, nameof(StageVictims.First), At.Head);
        Assert.True(patch.IsApplied, "the handle reports IsApplied false, so nothing was staged: " + Lines());
        Assert.Equal(3, StageVictims.Three());
        Assert.Single(StageVictims.Ran);
        Assert.False(
            ForkRuntime.IsDefaultBody(target),
            "the runtime still reports the default body while a composed body is staged, so the reading below " +
            "cannot tell a reverted method from a patched one.");

        patch.Dispose();

        Assert.Equal(3, StageVictims.Three());
        Assert.Single(StageVictims.Ran);
        Assert.True(
            ForkRuntime.IsDefaultBody(target),
            "the last handle is gone and the runtime still reports a body that is not the default one. Every " +
            "stage on the method left, so the runtime has to put the declared body back rather than leave the " +
            "last composition installed. " + Lines());
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void ADynamicMethodTargetComposes() {
        DynamicMethod victim = new DynamicMethod(
            "ConcordForkTests_StageDynamic",
            typeof(int),
            Type.EmptyTypes,
            typeof(StageTests).Module,
            skipVisibility: true);
        ILGenerator il = victim.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4, 41);
        il.Emit(OpCodes.Ret);
        Func<int> call = victim.CreateDelegate<Func<int>>();
        Assert.Equal(41, call());

        IPatchHandle patch = Patch(victim, nameof(StageVictims.First), At.Head);

        Assert.True(
            patch.IsApplied,
            "the handle reports IsApplied false, so nothing was staged over the dynamic method: " + Lines());
        Assert.Equal(41, call());
        Assert.Equal(
            new[] { "first" },
            StageVictims.Ran.ToArray());

        string[] reports = [.. lines.Where(line => line.StartsWith("[Concord.Waylith]", StringComparison.Ordinal))];
        Assert.True(
            reports.Length == 0,
            $"staging a DynamicMethod target wrote {reports.Length} '[Concord.Waylith]' lines and a clean compose " +
            "has to write none. " + Lines());

        patch.Dispose();

        Assert.Equal(41, call());
        Assert.Single(StageVictims.Ran);
        Assert.True(
            ForkRuntime.IsDefaultBody(victim),
            "the last handle is gone and the runtime still reports a body that is not the default one, so " +
            "disposing a patch on a dynamic method leaves the composition installed for the rest of the " +
            "process. " + Lines());
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void ADynamicMethodTargetAttributesTheHeadInjectionAndLeavesItsOwnBodyUnattributed() {
        DynamicMethod victim = new DynamicMethod(
            "ConcordForkTests_StageDynamicAttribution",
            typeof(int),
            Type.EmptyTypes,
            typeof(StageTests).Module,
            skipVisibility: true);
        ILGenerator il = victim.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4, 41);
        il.Emit(OpCodes.Ret);
        Func<int> call = victim.CreateDelegate<Func<int>>();
        Assert.Equal(41, call());

        IPatchHandle patch = Patch(victim, nameof(StageVictims.First), At.Head);
        registered.Add(victim);

        Assert.True(
            patch.IsApplied,
            "the handle reports IsApplied false, so nothing was staged over the dynamic method: " + Lines());
        Assert.Equal(
            typeof(StageVictims).FullName,
            FrameAttribution.OwnerAt(victim, 0));

        HostBody? installed = ForkBodyReader.Shared.ReadActive(victim);
        Assert.True(
            installed is not null,
            "the active body reader reports nothing for the dynamic method once it is staged, so the offset " +
            "below cannot be checked against a real body length. " + Lines());
        Assert.Null(FrameAttribution.OwnerAt(victim, installed!.Il.Length - 1));
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void AComposeTheStageRejectsLeavesTheHandleUnappliedAndLogsOneLine() {
        MethodInfo target = Victim(nameof(StageVictims.Rejected));

        IPatchHandle patch = ForkRuntime.Track(
            target,
            Patcher.PatchInjection(
                target,
                new Injection(
                    typeof(StageVictims).GetMethod(nameof(StageVictims.First))!,
                    new InjectAt.Invoke(typeof(StageVictims), nameof(StageVictims.Second), At.Head),
                    "stage-rejected",
                    0)));
        patched.Add(target);

        Assert.False(
            patch.IsApplied,
            "the handle reports IsApplied true for an injection the composer cannot bind, so a mod whose patch " +
            "never went in reads as patched. " + Lines());
        Assert.Equal(4, StageVictims.Rejected());
        Assert.Empty(StageVictims.Ran);

        string[] reports = [.. lines.Where(line => line.StartsWith("[Concord.Waylith]", StringComparison.Ordinal))];

        Assert.True(
            reports.Length == 1,
            $"a failed compose wrote {reports.Length} '[Concord.Waylith]' lines and has to write exactly one: a " +
            "silent failure leaves the mod author nothing to read, and a repeated one floods the log every time " +
            "something republishes the target. " + Lines());
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void ARejectedOwnerRollsOutOfLiveSoTheOtherOwnersInjectionStillRuns() {
        MethodInfo target = Victim(nameof(StageVictims.RejectedAmongOwners));

        IPatchHandle bad = ForkRuntime.Track(
            target,
            Patcher.PatchInjection(
                target,
                new Injection(
                    typeof(StageVictims).GetMethod(nameof(StageVictims.First))!,
                    new InjectAt.Invoke(typeof(StageVictims), nameof(StageVictims.Second), At.Head),
                    "owner-bad",
                    0)));
        patched.Add(target);

        IPatchHandle good = PatchAs(target, nameof(StageVictims.Second), new InjectAt.Head(), "owner-good");

        Assert.False(
            bad.IsApplied,
            "owner-bad's handle reports IsApplied true for an injection the composer cannot bind, so a mod " +
            "whose patch never went in reads as patched. " + Lines());
        Assert.True(
            good.IsApplied,
            "owner-good's handle reports IsApplied false even though its injection is the only one left in the " +
            "live set and republished cleanly. " + Lines());
        Assert.Equal(5, StageVictims.RejectedAmongOwners());
        Assert.Equal(
            new[] { "second" },
            StageVictims.Ran.ToArray());
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void TheCoreLibEnumDetoursApplyThroughTheStageAndUninstallRestoresTheDefaultBodies() {
        MethodInfo getName = typeof(Enum).GetMethod(
            "GetName",
            BindingFlags.Public | BindingFlags.Static,
            null,
            [typeof(Type), typeof(object)],
            null)!;

        ExtendedEnumRegistry.UseStore(new MemoryEnumStore());
        PatchDeclarationScanner.ScanExtendedEnums([typeof(StageWeatherExtension)]);

        Assert.True(
            ExtendedEnumRegistry.Extends(typeof(StageWeather)),
            "the registry does not report StageWeather as extended, so the scan allocated no member and the " +
            "detour below has nothing to resolve.");

        CoreLibEnumDetours.Install();
        try {
            Assert.False(
                ForkRuntime.IsDefaultBody(getName),
                "Enum.GetName still reports its default body after CoreLibEnumDetours.Install, so no detour " +
                "went in through the stage and the name below would be answered by the runtime, not by " +
                "Concord. " + Lines());
            Assert.Equal("Frozen", Enum.GetName(typeof(StageWeather), StageWeatherExtension.Frozen));
            Assert.Equal("Rain", Enum.GetName(typeof(StageWeather), StageWeather.Rain));
        } finally {
            CoreLibEnumDetours.Uninstall();
        }

        Assert.True(
            ForkRuntime.IsDefaultBody(getName),
            "Enum.GetName does not report its default body after Uninstall, so disposing the enum handles left " +
            "a composed body installed over a CoreLib method for the rest of the process.");
        Assert.Null(Enum.GetName(typeof(StageWeather), StageWeatherExtension.Frozen));
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void AnInjectionStillRunsAfterAFieldIsInjectedIntoItsDeclaringType() {
        MethodInfo target = Victim(nameof(StageVictims.ReadField));

        IPatchHandle patch = Patch(target, nameof(StageVictims.First), At.Head);
        Assert.True(patch.IsApplied, "the handle reports IsApplied false, so nothing was staged: " + Lines());
        Assert.Equal(7, ReadField(target, 7));
        Assert.Single(StageVictims.Ran);

        RelayoutOffThread(Field + "1");

        Assert.Equal(7, ReadField(target, 7));
        Assert.True(
            StageVictims.Ran.Count == 2,
            $"the injection ran {StageVictims.Ran.Count} times across two calls, one before a field was injected " +
            "into the type the body reads and one after, so the relayout took the patch down with it. A staged " +
            "body lives in the method's own metadata, so the runtime has to rejit it rather than poison it.");

        RelayoutOffThread(Field + "2");

        ReadField(target, 7);
        Assert.True(
            StageVictims.Ran.Count == 3,
            $"the injection ran {StageVictims.Ran.Count} times across three calls, one before either field " +
            "injection and one after each of the two relayouts, so the second relayout took the patch down " +
            "with it the same as the first. A staged body lives in the method's own metadata, so the runtime " +
            "has to rejit it rather than poison it.");
    }

    private static MethodInfo Victim(string name) {
        return typeof(StageVictims).GetMethod(name)!;
    }

    private static void RelayoutOffThread(string field) {
        string verdict = "the apply thread never ran";
        Thread worker = new Thread(() => verdict = Relayout(field));

        worker.Start();
        worker.Join();

        Assert.True(verdict == "Applied", verdict);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ReadField(MethodInfo target, int value) {
        return (int)ForkFieldInjection.Unwrapped(() => target.Invoke(null, [NewSubject(value)]))!;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object NewSubject(int value) {
        return new StageFieldSubject { Before = value };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string Relayout(string field) {
        int epoch = ForkFieldInjection.LayoutEpoch;
        ForkFieldInjection.Batch batch = ForkFieldInjection.NewBatch();
        batch.AddField(typeof(StageFieldSubject), field, typeof(int));
        batch.Commit();

        bool continued = false;
        ForkInjection.QueueForFrameBoundary(batch, () => continued = true);

        for (int attempt = 0; attempt < Attempts; attempt++) {
            ForkInjection.ApplyPendingAtFrameBoundary();
            if (batch.Status != "Queued" || !Transient.Contains(batch.LastRefusal)) {
                break;
            }

            Thread.Sleep(SettleMs);
        }

        return batch.Status == "Applied" && continued && epoch != ForkFieldInjection.LayoutEpoch
            ? "Applied"
            : batch.Verdict + $", the frame-boundary continuation {(continued ? "fired" : "never fired")}" +
                $", and the layout epoch went {epoch} to {ForkFieldInjection.LayoutEpoch}";
    }

    private IPatchHandle Patch(MethodBase target, string injection, At at) {
        IPatchHandle handle = ForkRuntime.Apply(target, typeof(StageVictims), injection, at);
        patched.Add(target);
        return handle;
    }

    private IPatchHandle PatchAs(MethodBase target, string injection, InjectAt at, string owner) {
        IPatchHandle handle = ForkRuntime.Track(
            target,
            Patcher.PatchInjection(
                target,
                new Injection(typeof(StageVictims).GetMethod(injection)!, at, owner, 0)));
        patched.Add(target);
        return handle;
    }

    private string Lines() {
        return lines.Count == 0 ? "Nothing was logged." : "Logged: " + string.Join(" | ", lines);
    }

    private sealed class MemoryEnumStore : IEnumValueStore {
        private Dictionary<string, long> saved = new Dictionary<string, long>(StringComparer.Ordinal);

        public bool TryLoad(out IReadOnlyDictionary<string, long> map) {
            map = new Dictionary<string, long>(saved, StringComparer.Ordinal);
            return true;
        }

        public void Save(IReadOnlyDictionary<string, long> map) {
            saved = new Dictionary<string, long>(map, StringComparer.Ordinal);
        }
    }
}
