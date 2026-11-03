using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Detour;
using Xunit;

namespace Concord.Host.Adapter.Tests;

public static class StaleBodySubject {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Value() {
        return 1;
    }
}

public static class StaleBodyStranger {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Value() {
        return 2;
    }
}

public static class StaleBodyInjections {
    public static int Ran;

    public static MethodBase? Wrapper;

    public static void Count() {
        Ran++;
        Wrapper ??= FirstFrameConcordOwns();
    }

    private static MethodBase? FirstFrameConcordOwns() {
        foreach (StackFrame frame in new StackTrace().GetFrames()) {
            MethodBase? method = frame.GetMethod();
            if (method is not null && MethodIdentity.ResolveOriginal(method) is not null) {
                return method;
            }
        }

        return null;
    }
}

public delegate bool StaleHandlerStandIn(MethodBase stale);

public static class WholeInjectionApi {
    public static bool IsSupported => true;

    public static void RegisterStaleMethodHandler(StaleHandlerStandIn handler) {
        _ = handler;
    }

    public static bool HasRewrittenBody(MethodBase method) {
        return method is null;
    }
}

public static class SwitchedOffInjectionApi {
    public static bool IsSupported => false;

    public static void RegisterStaleMethodHandler(StaleHandlerStandIn handler) {
        _ = handler;
    }

    public static bool HasRewrittenBody(MethodBase method) {
        return method is null;
    }
}

public static class NoSupportFlagInjectionApi {
    public static void RegisterStaleMethodHandler(StaleHandlerStandIn handler) {
        _ = handler;
    }

    public static bool HasRewrittenBody(MethodBase method) {
        return method is null;
    }
}

public sealed class InstanceSupportFlagInjectionApi {
    public bool IsSupported => true;

    public static void RegisterStaleMethodHandler(StaleHandlerStandIn handler) {
        _ = handler;
    }

    public static bool HasRewrittenBody(MethodBase method) {
        return method is null;
    }
}

public static class NoRegisterEntryInjectionApi {
    public static bool IsSupported => true;

    public static bool HasRewrittenBody(MethodBase method) {
        return method is null;
    }
}

public static class RewrittenVerdictIsNotABoolInjectionApi {
    public static bool IsSupported => true;

    public static void RegisterStaleMethodHandler(StaleHandlerStandIn handler) {
        _ = handler;
    }

    public static string HasRewrittenBody(MethodBase method) {
        return method is null ? "no" : "yes";
    }
}

public static class SupportFlagThatThrowsInjectionApi {
    public static bool IsSupported =>
        throw new EntryPointNotFoundException("Waylith_Injection_IsSupported is not exported by this runtime.");

    public static void RegisterStaleMethodHandler(StaleHandlerStandIn handler) {
        _ = handler;
    }

    public static bool HasRewrittenBody(MethodBase method) {
        return method is null;
    }
}

[Collection("AdapterStatics")]
public sealed class StaleBodyHandlerTests : IDisposable {
    private const string Thrown = "p3-t3 made this recompose throw on purpose";

    private static readonly List<string> Written = [];

    private static readonly MethodInfo Counter =
        typeof(StaleBodyInjections).GetMethod(nameof(StaleBodyInjections.Count))!;

    private readonly IDetourBackend backendBefore = DetourBackend.Current;

    private IDetourHandle? patched;

    public StaleBodyHandlerTests() {
        DetourBackend.Current = new MonoModDetourBackend();
        StaleBodyHandler.Install(Capture);
        Clear();
        StaleBodyInjections.Ran = 0;
        StaleBodyInjections.Wrapper = null;
    }

    public void Dispose() {
        StaleBodyHandler.RecomposeOverride = null;
        StaleBodyHandler.RewrittenBodyOverride = null;
        patched?.Dispose();
        patched = null;
        DetourBackend.Current = backendBefore;
        StaleBodyInjections.Ran = 0;
        StaleBodyInjections.Wrapper = null;
    }

    [Fact]
    public void ABodyTheHostItselfRewroteIsRefusedOutLoudAndNeverRecomposed() {
        MethodBase wrapper = PatchAndRunOnce();
        int asked = 0;
        StaleBodyHandler.RewrittenBodyOverride = _ => true;
        StaleBodyHandler.RecomposeOverride = _ => {
            asked++;
            return true;
        };

        bool answered = Handle(wrapper);

        Assert.True(
            !answered && asked == 0,
            $"the handler answered {answered} and reached the recompose {asked} time(s) for a body the host " +
            "reported rewritten. False and zero were wanted. Two writers on one body do not compose, because " +
            "publishing replaces rather than chains, so a recompose here silently drops whatever the host wrote; " +
            "and a true stops the chain, so harmony never regenerates its own patch on that method either.");
        Assert.True(
            Named(StaleBodyHandler.RefusedMarker) && Named(nameof(StaleBodySubject.Value)),
            $"no log line carries '{StaleBodyHandler.RefusedMarker}' and the refused method's name. Lines " +
            "written: " + Lines() + ". The refusal has to be loud and has to name the method: the player's " +
            "symptom is one patch that quietly stopped running, and this line is the only record of which " +
            "method Concord stepped away from.");
    }

    [Fact]
    public void AMethodConcordNeverPatchedIsNotReportedAsAFault() {
        MethodBase stranger = typeof(StaleBodyStranger).GetMethod(nameof(StaleBodyStranger.Value))!;
        StaleBodyHandler.RewrittenBodyOverride = _ => false;

        bool answered = Handle(stranger);

        Assert.True(
            !answered,
            $"the handler answered {answered} for a method Concord never patched, so it claimed a fix it cannot " +
            "have made. The chain stops on the first true, so harmony's handler would never run for a method only " +
            "harmony patched.");
        Assert.True(
            !Named(StaleBodyHandler.FaultMarker) && !Named(StaleBodyHandler.RefusedMarker)
            && !Named(StaleBodyHandler.NoRecomposeMarker),
            "the handler logged a refusal or a fault for a method Concord never patched: " + Lines() + ". Most " +
            "stale methods in a modded save belong to someone else, and the runtime reports every one of them, " +
            "so a line each would bury the real refusals and make the log useless for the case that matters.");
    }

    [Fact]
    public void ARecomposedTargetAnswersTrueSoNothingElseRebuildsIt() {
        MethodBase wrapper = PatchAndRunOnce();
        int asked = 0;
        StaleBodyHandler.RewrittenBodyOverride = _ => false;
        StaleBodyHandler.RecomposeOverride = _ => {
            asked++;
            return true;
        };

        bool answered = Handle(wrapper);

        Assert.True(
            answered && asked == 1,
            $"the handler answered {answered} after reaching the recompose {asked} time(s). True and one were " +
            "wanted. A false here sends the runtime on to the next handler, which rebuilds the same method a " +
            "second time and throws away the patch Concord just put back.");
    }

    [Fact]
    public void ARefusedRecomposeAnswersFalseSoHarmonysHandlerStillRuns() {
        MethodBase wrapper = PatchAndRunOnce();
        StaleBodyHandler.RewrittenBodyOverride = _ => false;
        StaleBodyHandler.RecomposeOverride = _ => false;

        bool answered = Handle(wrapper);

        Assert.True(
            !answered,
            $"the recompose refused and the handler still answered {answered}. False was wanted: the chain stops " +
            "on the first true, so claiming a fix Concord did not make leaves that method poisoned and skips " +
            "harmony's handler for a method harmony also patched.");
    }

    [Fact]
    public void ARecomposeThatThrowsIsSwallowedRatherThanKillingTheRestOfTheChain() {
        MethodBase wrapper = PatchAndRunOnce();
        int asked = 0;
        StaleBodyHandler.RewrittenBodyOverride = _ => false;
        StaleBodyHandler.RecomposeOverride = _ => {
            asked++;
            throw new InvalidOperationException(Thrown + ".");
        };

        bool answered = Handle(wrapper);

        Assert.True(
            !answered && asked == 1,
            $"the recompose threw, the handler reached it {asked} time(s) and answered {answered}. One and false " +
            "were wanted. Handle has to catch its own failure and answer honestly instead of letting it out.");
        Assert.True(
            Named(StaleBodyHandler.FaultMarker) && Named(Thrown),
            $"no log line carries '{StaleBodyHandler.FaultMarker}' and what the recompose threw. Lines " +
            "written: " + Lines() + ". A swallowed throw with no line reads exactly like a method Concord never " +
            "owned, so the patches it left poisoned would never be traced back to this failure.");
    }

    [Fact]
    public void ARefusedRowAnswersFalseEvenWhenTheHostLoggerThrowsOnEveryCall() {
        MethodBase wrapper = PatchAndRunOnce();
        StaleBodyHandler.RewrittenBodyOverride = _ => true;
        StaleBodyHandler.Install(ThrowingLogger);

        bool answered = Handle(wrapper);

        Assert.True(
            !answered,
            $"the handler answered {answered} for a refused body while the host's own logger threw on every " +
            "call. False was wanted: one try/catch wraps the runtime's whole handler chain, so a throw out of " +
            "Concord's own log call would end that chain and take harmony's handler down with it, same as a " +
            "throw from recompose.");
    }

    [Fact]
    public void HandlingOneTargetTwiceRecomposesItWithoutStackingASecondWrapper() {
        MethodBase first = PatchAndRunOnce();
        StaleBodyHandler.RewrittenBodyOverride = _ => false;

        bool once = Handle(first);

        Assert.True(
            MethodIdentity.ResolveOriginal(first) is null,
            $"the handler answered {once} and the wrapper it was handed still resolves to its target, so no new " +
            "wrapper replaced it. A recompose drops the old wrapper's keys and remembers the new one, so a " +
            "wrapper that still resolves means the handler claimed a rebuild it did not make, and the chain stops " +
            "on that claim while the method stays poisoned.");

        MethodBase second = RunOnceMore();
        bool twice = Handle(second);

        Assert.True(
            !ReferenceEquals(first, second),
            "the second recompose was handed the same wrapper instance as the first, so the target is running the " +
            "body it started with and this row measures one recompose rather than two.");
        Assert.True(
            once && twice,
            $"the real recompose path answered {once} then {twice} for one target handled twice. True both times " +
            "was wanted. The runtime can report one target on two relayouts in a single frame, and " +
            $"{nameof(IRecomposeTarget)} rebuilds from the injections already live rather than adding any, so the " +
            "second answer has to match the first. Lines written: " + Lines() + ".");

        StaleBodyInjections.Ran = 0;
        int read = StaleBodySubject.Value();

        Assert.True(
            read == 1 && StaleBodyInjections.Ran == 1,
            $"after two recomposes the target returned {read} and ran its one injection {StaleBodyInjections.Ran} " +
            "time(s). 1 and 1 were wanted. A count of 2 means the second recompose stacked a wrapper over the " +
            "first instead of replacing it, so every patched method in the save would run each mod's injection " +
            "one more time per field injection.");
    }

    [Fact]
    public void AnAbsentInjectionTypeIsTheAbsentGapAndCarriesNoProbeDetail() {
        (InjectionCapabilityGap gap, string? failure) = InjectionCapability.Probe(null, typeof(StaleHandlerStandIn));

        Assert.True(
            gap == InjectionCapabilityGap.AssemblyAbsent,
            $"a runtime with no injection type read {gap}. AssemblyAbsent was wanted. Any other gap reads as a " +
            "fork that is present and wrong, and the adapter's one log line would send a player looking for a " +
            "setting on a runtime that has no field injection at all.");
        Assert.True(
            failure is null,
            $"a runtime with no injection type at all reported the probe detail '{failure}'. Nothing was wanted: " +
            "the detail is reserved for an api that is present and threw, so filling it here reads as a broken " +
            "fork rather than a plain unmodded runtime.");
    }

    [Fact]
    public void AnApiWithoutAStaticBoolSupportFlagIsNotTheApiThisWasBuiltAgainst() {
        Unrecognized(typeof(NoSupportFlagInjectionApi), "exposes no IsSupported flag at all");
        Unrecognized(typeof(InstanceSupportFlagInjectionApi), "exposes IsSupported on an instance instead of the type");
    }

    [Fact]
    public void AFlagWithoutBothEntryPointsIsNotTheApiEither() {
        Unrecognized(typeof(NoRegisterEntryInjectionApi), "takes no handler registration");
        Unrecognized(
            typeof(RewrittenVerdictIsNotABoolInjectionApi),
            "answers HasRewrittenBody with something other than a bool");

        (InjectionCapabilityGap gap, string? failure) = InjectionCapability.Probe(typeof(WholeInjectionApi), null);

        Assert.True(
            gap == InjectionCapabilityGap.ApiUnrecognized && failure is null,
            $"a whole api whose handler delegate type is missing read {gap} with detail '{failure}'. " +
            "ApiUnrecognized and nothing were wanted: registration takes that exact delegate, so with no type to " +
            "build one there is nothing to register, and a None here has the adapter announce a channel it cannot " +
            "use.");
    }

    [Fact]
    public void AnApiThatReportsItselfOffIsToldApartFromOneThatIsMissing() {
        InjectionCapabilityGap off = Gap(typeof(SwitchedOffInjectionApi));
        InjectionCapabilityGap on = Gap(typeof(WholeInjectionApi));

        Assert.True(
            off == InjectionCapabilityGap.DisabledByRuntime && on == InjectionCapabilityGap.None,
            $"an api reporting IsSupported false read {off} and the same api reporting true read {on}. " +
            "DisabledByRuntime and None were wanted. Reading the off one as None has Concord register a handler " +
            "the runtime will never call, and reading the on one as anything else drops the only channel that " +
            "tells Concord its wrappers went stale.");
        Assert.True(
            InjectionCapability.Explain(off) != InjectionCapability.Explain(on),
            "a switched-off api and a working one explain themselves the same way, so the host log cannot tell " +
            "'field injection is off on this runtime' from 'Concord is watching for stale methods'. The first is " +
            "a setting the player can change and the second needs nothing from anyone.");
    }

    [Fact]
    public void ASupportFlagThatThrowsIsReportedAsAMismatchRatherThanTakingTheProcessDown() {
        (InjectionCapabilityGap gap, string? failure) =
            InjectionCapability.Probe(typeof(SupportFlagThatThrowsInjectionApi), typeof(StaleHandlerStandIn));

        Assert.True(
            gap == InjectionCapabilityGap.ApiUnrecognized
            && failure is not null
            && failure.Contains(nameof(EntryPointNotFoundException), StringComparison.Ordinal)
            && failure.Contains("Waylith_Injection_IsSupported", StringComparison.Ordinal),
            $"a support flag that threw read {gap} with detail '{failure}'. ApiUnrecognized naming the exception " +
            "and the entry point it could not find were wanted. The probe runs while the game is loading mods, so " +
            "letting that throw out takes the save down, and a verdict with no detail leaves nobody able to say " +
            "which export this runtime is missing.");
        Assert.True(
            InjectionCapability.Explain(gap, failure).Contains(failure!, StringComparison.Ordinal),
            $"the explanation for {gap} drops the detail it was handed, so the one line a player pastes into a " +
            "bug report says the api is the wrong shape without saying what went wrong reading it.");
        Assert.True(
            !InjectionCapability.Explain(gap).Contains(failure!, StringComparison.Ordinal),
            "the one-argument explanation carries a probe detail nobody handed it, so a caller holding only a gap " +
            "prints some other probe's failure next to its own verdict.");
    }

    [Fact]
    public void EveryGapExplainsItselfNamingThisRuntimeWithoutRepeatingAnother() {
        List<string> explained = [];

        foreach (InjectionCapabilityGap gap in Enum.GetValues<InjectionCapabilityGap>()) {
            string sentence = InjectionCapability.Explain(gap);

            Assert.True(
                sentence.Contains(RuntimeCapability.Runtime, StringComparison.Ordinal),
                $"{gap} explains itself as '{sentence}' without naming the runtime it read. A player's log holds " +
                "one line from one of several runtimes, so a line that does not say which one cannot be acted on.");
            Assert.True(
                !explained.Contains(sentence, StringComparer.Ordinal),
                $"{gap} explains itself exactly as an earlier gap does: '{sentence}'. Each gap has a different " +
                "fix, so two reading the same leaves the reader unable to tell which one they hit.");
            explained.Add(sentence);
        }

        Assert.True(
            explained.Count == Enum.GetValues<InjectionCapabilityGap>().Length,
            $"{explained.Count} of {Enum.GetValues<InjectionCapabilityGap>().Length} gaps were explained, so a " +
            "gap this build can report has no line of its own and the loop above never read it.");
    }

    [Fact]
    public void AGapThisBuildDoesNotDeclareIsRefusedRatherThanExplainedAsSomethingElse() {
        NotSupportedException refused = Assert.Throws<NotSupportedException>(
            () => InjectionCapability.Explain((InjectionCapabilityGap)99));

        Assert.True(
            refused.Message.Contains("99", StringComparison.Ordinal),
            $"an undeclared gap was refused with '{refused.Message}', which does not say which value it was. A " +
            "new gap added without its explanation has to name itself here, because the alternative is one of the " +
            "four existing lines printed for a verdict nobody wrote it for.");
    }

    [Fact]
    public void ReadingEveryCapabilityMemberOffForkNeitherThrowsNorClaimsTheChannel() {
        bool present = Type.GetType(InjectionCapability.InjectionTypeName, throwOnError: false) is not null;

        bool delegatePresent = Type.GetType(InjectionCapability.DelegateTypeName, throwOnError: false) is not null;

        Assert.True(
            InjectionCapability.Injection() is not null == present
            && InjectionCapability.Delegate() is not null == delegatePresent,
            "a type lookup disagrees with what the runtime holds, so the probe either throws on a runtime without " +
            "the fork or hands back a type nobody can bind. Both lookups have to answer null quietly, because " +
            "every unmodded runtime takes this path during mod loading.");
        Assert.True(
            InjectionCapability.Explain().Length > 0
            && InjectionCapability.Unavailable == (InjectionCapability.Gap != InjectionCapabilityGap.None),
            $"this process reads gap {InjectionCapability.Gap}, Unavailable {InjectionCapability.Unavailable} and " +
            "explains itself as '" + InjectionCapability.Explain() + "'. A blank explanation gives the install " +
            "path nothing to log, and an Unavailable that disagrees with the gap has the adapter skip the handler " +
            "on a runtime that supports it or register on one that does not.");

        if (present) {
            return;
        }

        Assert.True(
            InjectionCapability.Gap == InjectionCapabilityGap.AssemblyAbsent
            && InjectionCapability.ProbeFailure is null,
            $"off fork the probe read {InjectionCapability.Gap} with detail '{InjectionCapability.ProbeFailure}'. " +
            "AssemblyAbsent and no detail were wanted. Every other gap tells a player to change something, so " +
            "reporting one on a runtime that simply has no field injection sends them hunting a setting that does " +
            "not exist.");
    }

    private static void Unrecognized(Type injection, string shape) {
        InjectionCapabilityGap gap = Gap(injection);

        Assert.True(
            gap == InjectionCapabilityGap.ApiUnrecognized,
            $"an api that {shape} read {gap}. ApiUnrecognized was wanted: this build binds those members by " +
            "name, so a runtime shaped differently has to be told apart from one that works. Reading it as None " +
            "makes the adapter bind a member that is not there, and reading it as AssemblyAbsent hides a fork " +
            "that moved the api from the one line anybody would see.");
    }

    private static InjectionCapabilityGap Gap(Type injection) {
        (InjectionCapabilityGap gap, string? failure) =
            InjectionCapability.Probe(injection, typeof(StaleHandlerStandIn));

        Assert.True(
            failure is null,
            $"probing '{injection.Name}' captured the detail '{failure}'. Nothing was wanted: a shape mismatch is " +
            "read off the members, so a detail here means the probe called into the api and the verdict below " +
            "measures a throw rather than the shape.");
        return gap;
    }

    private static bool Handle(MethodBase stale) {
        try {
            return StaleBodyHandler.Handle(stale);
        } catch (Exception escaped) {
            Assert.Fail(
                $"Handle let a {escaped.GetType().Name} escape: {escaped.Message}. One try/catch wraps the whole " +
                "chain inside the runtime's RunStaleMethodHandlers, so a throw out of Concord's handler ends the " +
                "loop and every handler after it, harmony's included, never runs. Catching it inside Handle is " +
                "the only way the rest of the chain survives a bad day in Concord.");
            throw;
        }
    }

    private static void Capture(string line) {
        lock (Written) {
            Written.Add(line);
        }
    }

    private static void ThrowingLogger(string line) {
        _ = line;
        throw new InvalidOperationException(Thrown + ".");
    }

    private static void Clear() {
        lock (Written) {
            Written.Clear();
        }
    }

    private static bool Named(string token) {
        lock (Written) {
            return Written.Exists(line => line.Contains(token, StringComparison.Ordinal));
        }
    }

    private static string Lines() {
        lock (Written) {
            return Written.Count == 0 ? "none" : "'" + string.Join("' | '", Written) + "'";
        }
    }

    private MethodBase PatchAndRunOnce() {
        MethodBase target = typeof(StaleBodySubject).GetMethod(nameof(StaleBodySubject.Value))!;
        patched = DetourBackend.Current.ApplyComposed(
            target, [new Injection(Counter, new InjectAt.Head(), "p3-t3", 0)]);

        return RunOnceMore();
    }

    private MethodBase RunOnceMore() {
        StaleBodyInjections.Ran = 0;
        StaleBodyInjections.Wrapper = null;
        int read = StaleBodySubject.Value();

        Assert.True(
            read == 1 && StaleBodyInjections.Ran == 1 && patched?.IsApplied == true,
            $"the patched target returned {read} and ran its injection {StaleBodyInjections.Ran} time(s) with the " +
            $"patch applied={patched?.IsApplied}. 1, 1 and true were wanted. A target that is not really patched " +
            "has no wrapper for the runtime to call stale, so nothing below this line measures the handler.");
        Assert.True(
            StaleBodyInjections.Wrapper is not null
            && MethodIdentity.ResolveOriginal(StaleBodyInjections.Wrapper) is not null,
            "no frame inside the patched call resolves back through MethodIdentity.ResolveOriginal, so this file " +
            "has no stale method to hand the handler. The runtime reports the wrapper Concord composed, and that " +
            "reverse map is how the handler finds the target that owns it.");
        return StaleBodyInjections.Wrapper!;
    }
}
