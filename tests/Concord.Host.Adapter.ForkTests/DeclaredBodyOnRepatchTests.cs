using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Concord.Detour;
using Concord.Emit;
using HarmonyLib;
using Xunit;

namespace Concord.Host.Adapter.ForkTests;

public static class Repatched {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Read() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Doubled() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Natively() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Refused() {
        return 1;
    }
}

public static class Owners {
    public static int TenCalls;

    public static int HundredCalls;

    public static void PlusTen(ref int __result) {
        TenCalls++;
        __result += 10;
    }

    public static void PlusHundred(ref int __result) {
        HundredCalls++;
        __result += 100;
    }
}

public static class DetourNotify {
    private static readonly MonoModDetourBackend Native = new MonoModDetourBackend();

    public static MethodBase? Watched;

    public static int Intercepts;

    public static IDetourHandle? Installed;

    public static void Head(ControlHandle ch, MethodBase method, MethodBase replacement) {
        if (method == null || !ReferenceEquals(method, Watched)) {
            return;
        }

        Intercepts++;
        Installed?.Dispose();
        Installed = Native.Apply(method, (MethodInfo)replacement);
        ch.Cancel();
    }
}

[Collection(ForkFieldInjection.ApplyCollection)]
public sealed class DeclaredBodyOnRepatchTests : IDisposable {
    private const string NoFork = "No body-replacement runtime here, so nothing can be written.";

    private const string NoRead = "No host declared-body read here, so no declared body can be compared.";

    private const int LdcI4 = 0x20;

    private const int Ret = 0x2A;

    private static readonly MethodInfo Ten = typeof(Owners).GetMethod(nameof(Owners.PlusTen))!;

    private static readonly MethodInfo Hundred = typeof(Owners).GetMethod(nameof(Owners.PlusHundred))!;

    private readonly Harmony ownerA = new Harmony("concord.fork.repatch.a");

    private readonly Harmony ownerB = new Harmony("concord.fork.repatch.b");

    private readonly List<MethodBase> written = [];

    private readonly List<string> lines = [];

    private readonly Action<string> logged = WaylithDetourBackend.Log;

    private IDetourHandle? hook;

    public DeclaredBodyOnRepatchTests() {
        Owners.TenCalls = 0;
        Owners.HundredCalls = 0;
    }

    public static bool Unavailable => ForkRuntime.Unavailable || NotHarmonyThree;

    public static bool NoHostRead => ForkRuntime.Unavailable || !ForkRuntime.HostRead || NotHarmonyThree;

    public static bool NotHarmonyThree => typeof(Harmony).Assembly.GetName().Version?.Major != 3;

    public void Dispose() {
        WaylithDetourBackend.Log = logged;
        ownerA.UnpatchAll(ownerA.Id);
        ownerB.UnpatchAll(ownerB.Id);
        DetourNotify.Installed?.Dispose();
        DetourNotify.Installed = null;
        DetourNotify.Watched = null;
        DetourNotify.Intercepts = 0;
        hook?.Dispose();
        foreach (MethodBase target in written) {
            ForkRuntime.Restore(target);
        }
    }

    [Fact(Skip = NoRead, SkipWhen = nameof(NoHostRead))]
    public void ReflectionReportsTheWrittenBodyAndOnlyTheHostReadStillReportsTheDeclaredOne() {
        MethodInfo target = typeof(Repatched).GetMethod(nameof(Repatched.Read))!;

        byte[] before = Il(target);

        Assert.True(
            before.Length == 2,
            $"the untouched target reads back {before.Length} IL bytes ([{Hex(before)}]), and 'return 1' is " +
            "'ldc.i4.1; ret', which is two. Nothing below can tell a declared body from a written one if the " +
            "declared one is not the two bytes this fact assumes.");

        Write(target, 5);

        byte[] after = Il(target);

        Assert.Equal(5, Repatched.Read());
        Assert.True(
            after.Length == 6 && after[0] == LdcI4 && BitConverter.ToInt32(after, 1) == 5 && after[5] == Ret,
            $"MethodBase.GetMethodBody reports [{Hex(after)}] after Concord wrote 'ldc.i4 5; ret' over the " +
            $"target. Concord's body is [20 05 00 00 00 2A] and the declared one is [{Hex(before)}]. Harmony's " +
            "MethodBodyReader copies whatever this call answers, so this is the body every later Harmony rebuild " +
            "starts from.");

        byte[] declared = Il((MethodInfo)OriginalBody.AsDeclared(target));

        Assert.True(
            declared.Length == before.Length && declared[0] == before[0],
            $"OriginalBody.AsDeclared reports [{Hex(declared)}] over a written target and the declared body is " +
            $"[{Hex(before)}]. The host read is the only seam that still answers with declared IL, so a copier " +
            "put behind it is the only copier that sees the method as written in the assembly.");
    }

    [Fact(Skip = NoRead, SkipWhen = nameof(NoHostRead))]
    public void ASecondOwnerRepatchingOverAWrittenWrapperBodyFoldsTheFirstOwnerIn() {
        MethodInfo target = typeof(Repatched).GetMethod(nameof(Repatched.Doubled))!;

        Assert.Equal(1, Repatched.Doubled());

        ownerA.Patch(target, postfix: new HarmonyMethod(Ten));

        Assert.Equal(11, Repatched.Doubled());

        Write(target, 11);

        byte[] standIn = Il(target);

        Assert.True(
            standIn.Length == 6 && BitConverter.ToInt32(standIn, 1) == 11,
            $"the stand-in write reads back as [{Hex(standIn)}] rather than 'ldc.i4 11; ret'. This body stands in " +
            "for Concord installing owner A's wrapper by writing it over the target, which is what the composed " +
            "install does, and without it owner B has no wrapper body to copy and this fact measures nothing.");

        ownerB.Patch(target, postfix: new HarmonyMethod(Hundred));

        int result = Repatched.Doubled();

        Assert.True(
            Owners.TenCalls > 0 && Owners.HundredCalls > 0,
            $"owner A's postfix ran {Owners.TenCalls} times and owner B's {Owners.HundredCalls}. Neither patch " +
            "landed, so the number below is not a composition of anything.");
        Assert.True(
            result == 121,
            $"the twice-patched target returns {result}. 121 is the written stand-in's 1 with A's 10 already baked " +
            "in, rebuilt by owner B's patch and folded with B's 100 on top, so A's 10 lands twice. 111 would be A " +
            "and B applied once each over the declared body, which is unreachable here: Harmony's MethodBodyReader " +
            "copies the live IL this backend wrote, not the declared body OriginalBody.AsDeclared can still see, " +
            "so the second owner to patch a body-written target always folds the first owner's wrapper into its " +
            "own.");
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void ASecondOwnerRepatchingOverANativelyInstalledWrapperAppliesTheFirstOwnerOnce() {
        MethodInfo target = typeof(Repatched).GetMethod(nameof(Repatched.Natively))!;
        DetourNotify.Watched = target;
        InstallHook();

        ownerA.Patch(target, postfix: new HarmonyMethod(Ten));

        Assert.Equal(1, DetourNotify.Intercepts);
        Assert.Equal(11, Repatched.Natively());

        ownerB.Patch(target, postfix: new HarmonyMethod(Hundred));

        Assert.Equal(2, DetourNotify.Intercepts);

        int result = Repatched.Natively();

        Assert.True(
            Owners.TenCalls > 0 && Owners.HundredCalls > 0,
            $"owner A's postfix ran {Owners.TenCalls} times and owner B's {Owners.HundredCalls}. Neither patch " +
            "landed through the intercepted install, so the number below composes nothing.");
        Assert.True(
            result == 111,
            $"the twice-patched target returns {result} when Concord installs Harmony's own wrapper as a native " +
            "detour instead of writing it over the body. 111 is A and B applied once each over the declared body, " +
            "and 121 would be A counted twice. A native install leaves MethodBase.GetMethodBody reporting the " +
            "declared IL, so Harmony's copier never sees an earlier wrapper and the doubling cannot arise.");
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void ABodyWriteCannotInstallHarmonysWrapperBecauseAWrittenBodyCannotCallADynamicMethod() {
        MethodInfo target = typeof(Repatched).GetMethod(nameof(Repatched.Refused))!;
        DynamicMethod wrapper = new DynamicMethod("repatch-wrapper", typeof(int), Type.EmptyTypes);
        ILGenerator il = wrapper.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4, 9);
        il.Emit(OpCodes.Ret);

        RefusingBackend fallback = new RefusingBackend();
        WaylithDetourBackend backend = new WaylithDetourBackend(
            fallback, new ComposedBodyPublisher(fallback), ForkBodyWriter.Shared);
        WaylithDetourBackend.Log = lines.Add;

        Exception refusal = Record.Exception(() => backend.Apply(target, wrapper))
            ?? throw new InvalidOperationException(
                "the body writer accepted a dynamic-method replacement, so the forwarder it wrote names a method " +
                "with no metadata token and this fact's premise is wrong.");

        Assert.True(
            fallback.HandedOff.Count == 1 && ReferenceEquals(fallback.HandedOff[0], target),
            $"the backend handed {fallback.HandedOff.Count} targets to the fallback. A refusal to write the body " +
            $"has to end up at the native fallback, and the refusal reported was: {refusal.Message}");
        Assert.True(
            lines.Exists(line => line.Contains("dynamic", StringComparison.Ordinal)),
            "the backend conceded the target without saying the replacement is a dynamic method. It logged: " +
            (lines.Count == 0 ? "nothing" : string.Join(" | ", lines)));
        Assert.True(
            Il(target).Length == 2 && Repatched.Refused() == 1,
            "the conceded target no longer reads back as its declared body, so a forwarder was written over it " +
            "after all and the concession above did not stop the write.");
    }

    private static byte[] Il(MethodInfo target) {
        return target.GetMethodBody()?.GetILAsByteArray()
            ?? throw new InvalidOperationException(
                $"'{target.Name}' reports no method body at all, so nothing here can read its IL.");
    }

    private static string Hex(byte[] il) {
        return BitConverter.ToString(il).Replace('-', ' ');
    }

    private void InstallHook() {
        MethodInfo detourMethod = typeof(Harmony).Assembly
            .GetType("HarmonyLib.PatchTools")!
            .GetMethod("DetourMethod", BindingFlags.NonPublic | BindingFlags.Static)!;

        MethodInfo notify = typeof(DetourNotify).GetMethod(nameof(DetourNotify.Head))!;
        IDetourBackend backend = new WaylithDetourBackend(new MonoModDetourBackend());
        hook = backend.ApplyComposed(detourMethod, [new Injection(notify, new InjectAt.Head(), "repatch-probe", 0)]);
    }

    private void Write(MethodBase target, int value) {
        ForkRuntime.Write(target, value);
        written.Add(target);
    }
}
