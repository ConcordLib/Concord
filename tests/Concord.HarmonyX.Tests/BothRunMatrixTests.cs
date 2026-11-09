using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Concord.Backend;
using Concord.Emit;
using HarmonyLib;
using Xunit;

namespace Concord.HarmonyX.Tests;

public static class BothRunTargets {
    public static readonly List<string> Ran = [];

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Step(int value) {
        return value;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int M00(int seed) {
        Ran.Add("body");
        return Step(seed) + 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int M01(int seed) {
        Ran.Add("body");
        return Step(seed) + 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int M02(int seed) {
        Ran.Add("body");
        return Step(seed) + 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int M03(int seed) {
        Ran.Add("body");
        return Step(seed) + 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int M04(int seed) {
        Ran.Add("body");
        return Step(seed) + 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int M05(int seed) {
        Ran.Add("body");
        return Step(seed) + 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int M06(int seed) {
        Ran.Add("body");
        return Step(seed) + 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int M07(int seed) {
        Ran.Add("body");
        return Step(seed) + 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int M08(int seed) {
        Ran.Add("body");
        return Step(seed) + 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int M09(int seed) {
        Ran.Add("body");
        return Step(seed) + 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int M10(int seed) {
        Ran.Add("body");
        return Step(seed) + 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int M11(int seed) {
        Ran.Add("body");
        return Step(seed) + 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int M12(int seed) {
        Ran.Add("body");
        return Step(seed) + 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int M13(int seed) {
        Ran.Add("body");
        return Step(seed) + 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int M14(int seed) {
        Ran.Add("body");
        return Step(seed) + 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int M15(int seed) {
        Ran.Add("body");
        return Step(seed) + 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int M16(int seed) {
        Ran.Add("body");
        return Step(seed) + 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int M17(int seed) {
        Ran.Add("body");
        return Step(seed) + 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int M18(int seed) {
        Ran.Add("body");
        return Step(seed) + 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int M19(int seed) {
        Ran.Add("body");
        return Step(seed) + 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int M20(int seed) {
        Ran.Add("body");
        return Step(seed) + 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int M21(int seed) {
        Ran.Add("body");
        return Step(seed) + 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int M22(int seed) {
        Ran.Add("body");
        return Step(seed) + 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int M23(int seed) {
        Ran.Add("body");
        return Step(seed) + 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int M24(int seed) {
        Ran.Add("body");
        return Step(seed) + 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int M25(int seed) {
        Ran.Add("body");
        return Step(seed) + 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int M26(int seed) {
        Ran.Add("body");
        return Step(seed) + 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int M27(int seed) {
        Ran.Add("body");
        return Step(seed) + 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int M28(int seed) {
        Ran.Add("body");
        return Step(seed) + 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int M29(int seed) {
        Ran.Add("body");
        return Step(seed) + 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int M30(int seed) {
        Ran.Add("body");
        return Step(seed) + 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int M31(int seed) {
        Ran.Add("body");
        return Step(seed) + 100;
    }
}

public static class BothRunMods {
    public static void ConcordNote() {
        BothRunTargets.Ran.Add("concord");
    }

    public static void HarmonyPrefix() {
        BothRunTargets.Ran.Add("harmony");
    }

    public static void HarmonyPostfix() {
        BothRunTargets.Ran.Add("harmony");
    }

    public static void HarmonyFinalizer() {
        BothRunTargets.Ran.Add("harmony");
    }

    public static IEnumerable<HarmonyLib.CodeInstruction> HarmonyTranspiler(IEnumerable<HarmonyLib.CodeInstruction> instructions) {
        foreach (HarmonyLib.CodeInstruction instruction in instructions) {
            if (LoadsOneHundred(instruction)) {
                yield return new HarmonyLib.CodeInstruction(OpCodes.Ldc_I4, 200);
                continue;
            }

            yield return instruction;
        }
    }

    private static bool LoadsOneHundred(HarmonyLib.CodeInstruction instruction) {
        return instruction.operand is sbyte or byte or short or int
            && Convert.ToInt64(instruction.operand, CultureInfo.InvariantCulture) == 100L;
    }
}

public sealed class BothRunMatrixTests {
    [Theory]
    [InlineData("M00", "Head", "Prefix", "ConcordFirst")]
    [InlineData("M01", "Head", "Prefix", "HarmonyFirst")]
    [InlineData("M02", "Head", "Postfix", "ConcordFirst")]
    [InlineData("M03", "Head", "Postfix", "HarmonyFirst")]
    [InlineData("M04", "Head", "Transpiler", "ConcordFirst")]
    [InlineData("M05", "Head", "Transpiler", "HarmonyFirst")]
    [InlineData("M06", "Head", "Finalizer", "ConcordFirst")]
    [InlineData("M07", "Head", "Finalizer", "HarmonyFirst")]
    [InlineData("M08", "Return", "Prefix", "ConcordFirst")]
    [InlineData("M09", "Return", "Prefix", "HarmonyFirst")]
    [InlineData("M10", "Return", "Postfix", "ConcordFirst")]
    [InlineData("M11", "Return", "Postfix", "HarmonyFirst")]
    [InlineData("M12", "Return", "Transpiler", "ConcordFirst")]
    [InlineData("M13", "Return", "Transpiler", "HarmonyFirst")]
    [InlineData("M14", "Return", "Finalizer", "ConcordFirst")]
    [InlineData("M15", "Return", "Finalizer", "HarmonyFirst")]
    [InlineData("M16", "Tail", "Prefix", "ConcordFirst")]
    [InlineData("M17", "Tail", "Prefix", "HarmonyFirst")]
    [InlineData("M18", "Tail", "Postfix", "ConcordFirst")]
    [InlineData("M19", "Tail", "Postfix", "HarmonyFirst")]
    [InlineData("M20", "Tail", "Transpiler", "ConcordFirst")]
    [InlineData("M21", "Tail", "Transpiler", "HarmonyFirst")]
    [InlineData("M22", "Tail", "Finalizer", "ConcordFirst")]
    [InlineData("M23", "Tail", "Finalizer", "HarmonyFirst")]
    [InlineData("M24", "Invoke", "Prefix", "ConcordFirst")]
    [InlineData("M25", "Invoke", "Prefix", "HarmonyFirst")]
    [InlineData("M26", "Invoke", "Postfix", "ConcordFirst")]
    [InlineData("M27", "Invoke", "Postfix", "HarmonyFirst")]
    [InlineData("M28", "Invoke", "Transpiler", "ConcordFirst")]
    [InlineData("M29", "Invoke", "Transpiler", "HarmonyFirst")]
    [InlineData("M30", "Invoke", "Finalizer", "ConcordFirst")]
    [InlineData("M31", "Invoke", "Finalizer", "HarmonyFirst")]
    public void EveryConcordPosition_RunsBesideEveryHarmonyPatchKind_InBothArrivalOrders(
        string targetName,
        string position,
        string kind,
        string order) {
        List<string> lines = [];
        MethodInfo target = typeof(BothRunTargets).GetMethod(targetName)!;
        string id = "test.bothrun.harmonyx." + targetName;
        Harmony foreign = new Harmony(id);
        IBackendHandle? handle = null;

        try {
            HarmonyXEntryPoint.Install(lines.Add, null);
            BothRunTargets.Ran.Clear();

            if (order == "HarmonyFirst") {
                Patch(foreign, target, kind);
                handle = PatchBackend.Current.ApplyComposed(target, new[] { Concord(position) });
            } else {
                handle = PatchBackend.Current.ApplyComposed(target, new[] { Concord(position) });
                Patch(foreign, target, kind);
            }

            Assert.True(handle.IsApplied, "Concord's handle is not applied. " + Lines(lines));

            BothRunTargets.Ran.Clear();
            int produced = (int)target.Invoke(null, new object?[] { 1 })!;

            int expected = kind == "Transpiler" ? 201 : 101;
            Assert.Equal(expected, produced);
            Assert.Contains("concord", BothRunTargets.Ran);
            Assert.Contains("body", BothRunTargets.Ran);

            if (kind != "Transpiler") {
                Assert.Contains("harmony", BothRunTargets.Ran);
            }
        } finally {
            handle?.Dispose();
            foreign.UnpatchSelf();
        }
    }

    private static void Patch(Harmony foreign, MethodInfo target, string kind) {
        switch (kind) {
            case "Prefix":
                foreign.Patch(target, prefix: new HarmonyMethod(Mod(nameof(BothRunMods.HarmonyPrefix))));
                return;
            case "Postfix":
                foreign.Patch(target, postfix: new HarmonyMethod(Mod(nameof(BothRunMods.HarmonyPostfix))));
                return;
            case "Transpiler":
                foreign.Patch(target, transpiler: new HarmonyMethod(Mod(nameof(BothRunMods.HarmonyTranspiler))));
                return;
            case "Finalizer":
                foreign.Patch(target, finalizer: new HarmonyMethod(Mod(nameof(BothRunMods.HarmonyFinalizer))));
                return;
            default:
                throw new InvalidOperationException("unknown Harmony patch kind " + kind);
        }
    }

    private static Injection Concord(string position) {
        MethodInfo note = Mod(nameof(BothRunMods.ConcordNote));

        InjectAt at = position switch {
            "Head" => new InjectAt.Head(),
            "Return" => new InjectAt.Return(),
            "Tail" => new InjectAt.Tail(),
            "Invoke" => new InjectAt.Invoke(typeof(BothRunTargets), nameof(BothRunTargets.Step), At.Head),
            _ => throw new InvalidOperationException("unknown Concord position " + position),
        };

        return new Injection(note, at, "test.bothrun.concord", 0);
    }

    private static MethodInfo Mod(string name) {
        return typeof(BothRunMods).GetMethod(name)!;
    }

    private static string Lines(List<string> lines) {
        return lines.Count == 0 ? "Nothing was logged." : "Logged: " + string.Join(" | ", lines);
    }
}
