using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;
using Xunit;

namespace Concord.Host.Adapter.ForkTests;

public static class DetourVictims {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Ordinary() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Bookkept() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Reversed() {
        return 2;
    }
}

public static class ReverseStandin {
    public static int Reversed() {
        throw new NotImplementedException("The reverse-patch stand-in ran its own body, so nothing was installed over it.");
    }
}

public sealed record DetourCall(MethodBase Method, MethodBase Replacement);

public static class DetourInstall {
    public static readonly List<DetourCall> Calls = [];

    public static readonly HashSet<MethodBase> Claimed = [];

    private static readonly Dictionary<MethodBase, IDetourHandle> Installed = [];

    public static IDetourBackend? Backend { get; set; }

    public static string? Failure { get; private set; }

    public static void Claim(MethodBase method) {
        Claimed.Add(method);
    }

    public static void Head(ControlHandle ch, MethodBase method, MethodBase replacement) {
        if (method is null || Backend is null || !Claimed.Contains(method)) {
            return;
        }

        Calls.Add(new DetourCall(method, replacement));
        if (!TryInstall(method, replacement)) {
            return;
        }

        ch.Cancel();
    }

    public static void Reset() {
        Claimed.Clear();
        Backend = null;
        foreach (IDetourHandle handle in Installed.Values) {
            try {
                handle.Dispose();
            } catch (Exception ex) {
                Failure ??= ex.ToString();
            }
        }

        Installed.Clear();
        Calls.Clear();
        Failure = null;
    }

    public static bool TryInstall(MethodBase method, MethodBase replacement) {
        try {
            if (Installed.Remove(method, out IDetourHandle? previous)) {
                previous.Dispose();
            }

            Installed[method] = Backend!.Apply(method, (MethodInfo)replacement);
            return true;
        } catch (Exception ex) {
            Failure = ex.ToString();
            return false;
        }
    }
}

[Collection(ForkFieldInjection.ApplyCollection)]
public sealed class DetourMethodInstallTests : IDisposable {
    private const string NoFork = "No body-replacement runtime here, so nothing can be installed over a method.";

    private static readonly MethodInfo Shifter = typeof(Shift).GetMethod(nameof(Shift.Plus10))!;

    private readonly Harmony harmony = new Harmony("concord.fork.detour-install");

    private readonly List<string> logged = [];

    private readonly Action<string> priorLog = WaylithDetourBackend.Log;

    private IDetourHandle? hook;

    public DetourMethodInstallTests() {
        WaylithDetourBackend.Log = message => {
            lock (logged) {
                logged.Add(message);
            }
        };
    }

    public static bool Unavailable => ForkRuntime.Unavailable || NotHarmonyThree;

    public static bool NotHarmonyThree => typeof(Harmony).Assembly.GetName().Version?.Major != 3;

    public void Dispose() {
        DetourInstall.Reset();
        hook?.Dispose();
        harmony.UnpatchAll(harmony.Id);
        WaylithDetourBackend.Log = priorLog;
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void AnInstallingInjectionOnDetourMethodCarriesAnOrdinaryHarmonyPatch() {
        MethodInfo target = typeof(DetourVictims).GetMethod(nameof(DetourVictims.Ordinary))!;
        DetourInstall.Claim(target);
        InstallHook();

        harmony.Patch(target, postfix: new HarmonyMethod(Shifter));

        int returned = DetourVictims.Ordinary();

        Assert.True(
            DetourInstall.Calls.Count == 1 && DetourInstall.Failure is null && returned == 11,
            Report(
                $"one Harmony patch on a NoInlining victim fired the DetourMethod injection {DetourInstall.Calls.Count} " +
                $"time(s) and the patched victim returned {returned}, wanted one firing and 11. The injection installs " +
                "Harmony's own wrapper through Concord's backend and cancels, so Harmony's " +
                "DetourFactory.Current.CreateDetour never runs. A return of 1 means nothing was installed, and 11 " +
                "means Harmony composed the wrapper and Concord carried it."));
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void CancellingDetourMethodLeavesHarmonysDetourDictionaryEmptyForTheTarget() {
        MethodInfo target = typeof(DetourVictims).GetMethod(nameof(DetourVictims.Bookkept))!;
        DetourInstall.Claim(target);
        InstallHook();

        harmony.Patch(target, postfix: new HarmonyMethod(Shifter));

        IDictionary detours = HarmonyDetours();
        bool tracked;
        int count;
        lock (detours) {
            tracked = detours.Contains(target);
            count = detours.Count;
        }

        Patches? patches = Harmony.GetPatchInfo(target);
        int postfixes = patches?.Postfixes.Count ?? 0;

        Assert.True(
            !tracked && postfixes == 1,
            Report(
                $"after an installed-and-cancelled patch, HarmonyLib.PatchTools.detours {(tracked ? "holds" : "does not hold")} " +
                $"an entry for the target ({count} entr(ies) in total) and Harmony recorded {postfixes} postfix(es) for it. " +
                "Cancelling DetourMethod skips Harmony's only write to that dictionary, and the cost is bounded: the " +
                "field is read in PatchTools.DetourMethod and written in the PatchTools static constructor, nowhere " +
                "else in 0Harmony. So the one thing lost is the ICoreDetour handle Harmony would have Disposed on the " +
                "next patch of the same method, which the installer has to dispose itself. UpdateWrapper still calls " +
                "HarmonySharedState.UpdatePatchInfo after DetourMethod returns, so patch info, ordering and " +
                "Harmony.GetPatchInfo stay intact."));
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void AReversePatchReachesTheSameInjectionAndHandsItTheUserStub() {
        MethodInfo victim = typeof(DetourVictims).GetMethod(nameof(DetourVictims.Reversed))!;
        MethodInfo standin = typeof(ReverseStandin).GetMethod(nameof(ReverseStandin.Reversed))!;
        DetourInstall.Claim(victim);
        DetourInstall.Claim(standin);
        InstallHook();

        MethodInfo written = harmony
            .CreateReversePatcher(victim, new HarmonyMethod(standin))
            .Patch(HarmonyReversePatchType.Original);

        DetourCall? call = DetourInstall.Calls.Count == 1 ? DetourInstall.Calls[0] : null;
        bool sawStandin = call is not null && ReferenceEquals(call.Method, standin);
        bool sawGenerated = call is not null && ReferenceEquals(call.Replacement, written);

        Assert.True(
            sawStandin && sawGenerated && DetourInstall.Failure is null,
            Report(
                $"a Harmony reverse patch fired the DetourMethod injection {DetourInstall.Calls.Count} time(s) and " +
                $"handed it method '{Describe(call?.Method)}' with replacement '{Describe(call?.Replacement)}', wanted " +
                $"one firing with method '{Describe(standin)}' and replacement '{Describe(written)}'. " +
                "PatchFunctions.ReversePatch calls DetourMethod directly, so the same injection covers it, but the two " +
                "arguments are not what a patch of an ordinary method hands over: 'method' is the mod author's own " +
                "stand-in stub and 'replacement' is the generated wrapper. An installer may not assume 'method' is a " +
                "game method, nor that it is something Harmony has patch info for."));

        int standinReturned = (int)standin.Invoke(null, null)!;

        Assert.True(
            standinReturned == 2,
            Report(
                $"the reverse-patched stand-in returned {standinReturned}, wanted 2, the victim's own value. The " +
                "stand-in's own body throws, so a value at all means Concord's install carried Harmony's generated " +
                "wrapper onto it."));
    }

    private static IDictionary HarmonyDetours() {
        FieldInfo field = typeof(Harmony).Assembly
            .GetType("HarmonyLib.PatchTools")!
            .GetField("detours", BindingFlags.NonPublic | BindingFlags.Static)!;

        return (IDictionary)field.GetValue(null)!;
    }

    private static string Describe(MethodBase? method) {
        if (method is null) {
            return "<none>";
        }

        return (method.DeclaringType?.FullName ?? method.Module.Name) + "." + method.Name;
    }

    private void InstallHook() {
        MethodInfo detourMethod = typeof(Harmony).Assembly
            .GetType("HarmonyLib.PatchTools")!
            .GetMethod("DetourMethod", BindingFlags.NonPublic | BindingFlags.Static)!;

        MethodInfo notify = typeof(DetourInstall).GetMethod(nameof(DetourInstall.Head))!;
        IDetourBackend backend = new WaylithDetourBackend(new MonoModDetourBackend());
        DetourInstall.Backend = backend;
        hook = backend.ApplyComposed(detourMethod, [new Injection(notify, new InjectAt.Head(), "probe", 0)]);
    }

    private string Report(string finding) {
        List<string> lines = [finding];
        foreach (DetourCall call in DetourInstall.Calls) {
            Assembly home = call.Replacement.Module.Assembly;
            lines.Add(
                $"install: method '{Describe(call.Method)}', replacement '{Describe(call.Replacement)}' " +
                $"({call.Replacement.GetType().Name}, dynamic assembly {home.IsDynamic}, '{home.FullName}'), " +
                $"Concord holds a body claim on the method: {WaylithDetourBackend.Writers.HoldsClaim(call.Method)}. " +
                "A held claim means Concord wrote a forwarder body over the method. No claim means the install fell " +
                "back to a native detour through Concord's own repacked MonoMod, which is what happens here: " +
                "Harmony's wrapper is a DynamicMethod, so the runtime's token api refuses to name it from a replaced " +
                "body ('MethodInfo must be a runtime MethodInfo object'). Harmony's copy of MonoMod never runs either " +
                "way, but the install is a native detour, not a body replacement, so it does not carry the " +
                "inlined-caller and hot-caller guarantees a replaced body has.");
        }

        if (DetourInstall.Failure is string failure) {
            lines.Add("install failure: " + failure);
        }

        lock (logged) {
            foreach (string message in logged) {
                lines.Add("log: " + message);
            }
        }

        return string.Join(Environment.NewLine, lines);
    }
}
