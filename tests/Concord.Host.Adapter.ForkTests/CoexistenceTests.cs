using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Xunit;

namespace Concord.Host.Adapter.ForkTests;

public static class Victims {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Written() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int PatchedThenWritten() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int WrittenThenPatched() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int WrittenThenUnpatched() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Poisoned() {
        return 1;
    }
}

public static class Shift {
    public static void Plus10(ref int __result) {
        __result += 10;
    }
}

public sealed class CoexistenceTests : IDisposable {
    private const string NoFork = "No body-replacement runtime here, so nothing can be written.";

    private static readonly MethodInfo Shifter = typeof(Shift).GetMethod(nameof(Shift.Plus10))!;

    private readonly Harmony harmony = new Harmony("concord.fork.coexistence");

    private readonly List<MethodBase> written = [];

    public static bool Unavailable => ForkRuntime.Unavailable;

    public void Dispose() {
        harmony.UnpatchAll(harmony.Id);
        foreach (MethodBase target in written) {
            ForkRuntime.Restore(target);
        }
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void AWrittenBodyTakesEffectAndRevertsCleanly() {
        MethodInfo target = typeof(Victims).GetMethod(nameof(Victims.Written))!;

        Assert.Equal(1, Victims.Written());
        Write(target, 5);
        Assert.Equal(5, Victims.Written());
        ForkRuntime.Restore(target);
        written.Remove(target);
        Assert.Equal(1, Victims.Written());
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void AHarmonyPatchHidesABodyWrittenAfterIt() {
        MethodInfo target = typeof(Victims).GetMethod(nameof(Victims.PatchedThenWritten))!;

        harmony.Patch(target, postfix: new HarmonyMethod(Shifter));
        Assert.Equal(11, Victims.PatchedThenWritten());

        Write(target, 7);

        Assert.Equal(11, Victims.PatchedThenWritten());
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void AHarmonyPatchComposesOntoTheWrittenBodyNotTheDeclaredOne() {
        MethodInfo target = typeof(Victims).GetMethod(nameof(Victims.WrittenThenPatched))!;

        Write(target, 5);
        Assert.Equal(5, Victims.WrittenThenPatched());

        harmony.Patch(target, postfix: new HarmonyMethod(Shifter));

        int patched = Victims.WrittenThenPatched();

        Assert.True(
            patched == 15,
            $"the patched target returns {patched}. 15 is the written body's 5 plus the postfix, and 11 would be " +
            "the declared body's 1 plus the postfix. Harmony copies a target through MethodBase.GetMethodBody, " +
            "which this runtime answers with whatever body is live, so a foreign patcher landing after Concord " +
            "folds Concord's composed body into its own copy instead of starting from the raw IL.");
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void AHarmonyUnpatchRestoresTheWrittenBodyNotTheDeclaredOne() {
        MethodInfo target = typeof(Victims).GetMethod(nameof(Victims.WrittenThenUnpatched))!;

        Write(target, 5);
        harmony.Patch(target, postfix: new HarmonyMethod(Shifter));
        harmony.Unpatch(target, Shifter);

        int unpatched = Victims.WrittenThenUnpatched();

        Assert.True(
            unpatched == 5,
            $"the unpatched target returns {unpatched}. 5 is the body Concord wrote and 1 is the declared one. " +
            "An unpatch rebuilds from the copy Harmony took, so Concord's body survives a foreign unpatch here " +
            "rather than being discarded by it.");
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void AHarmonyPatchPoisonsAMethodForEveryLaterBodyWrite() {
        MethodInfo target = typeof(Victims).GetMethod(nameof(Victims.Poisoned))!;

        harmony.Patch(target, postfix: new HarmonyMethod(Shifter));
        harmony.Unpatch(target, Shifter);
        Assert.Equal(1, Victims.Poisoned());

        Write(target, 5);

        Assert.Equal(1, Victims.Poisoned());
    }

    private void Write(MethodBase target, int value) {
        ForkRuntime.Write(target, value);
        written.Add(target);
    }
}
