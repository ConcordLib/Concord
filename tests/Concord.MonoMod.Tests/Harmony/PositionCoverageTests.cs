using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Concord;
using Concord.Backend;
using Concord.Emit;
using Concord.MonoMod;
using HarmonyLib;
using Xunit;

namespace Concord.MonoMod.Tests;

public sealed class PositionCoverageSubject {
    public int Value;
}

public static class PositionCoverageTargets {
    public static readonly List<string> Ran = [];

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Step(int value) {
        return value + 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int CallsStep() {
        return Step(1);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int MakesSubject() {
        PositionCoverageSubject subject = new PositionCoverageSubject { Value = 3 };
        return subject.Value;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ReadsNamedLocal() {
        int held = 5;
        return held + 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int RetunedFinal() {
        return 100;
    }
}

public static class PositionCoverageMods {
    public static void ForeignNoop() {
        PositionCoverageTargets.Ran.Add("foreign");
    }

    public static int ClampArg(int original) {
        int seen = original;
        PositionCoverageTargets.Ran.Add("arg:" + seen.ToString(CultureInfo.InvariantCulture));
        return seen + 10;
    }

    public static void Note() {
        PositionCoverageTargets.Ran.Add("note");
    }

    public static int LogHeld(int original) {
        int seen = original;
        PositionCoverageTargets.Ran.Add("read:" + seen.ToString(CultureInfo.InvariantCulture));
        return seen + 100;
    }

    public static IEnumerable<CodeInstruction> Retune(IEnumerable<CodeInstruction> instructions) {
        PositionCoverageTargets.Ran.Add("transpiler");
        foreach (CodeInstruction instruction in instructions) {
            if (LoadsHundred(instruction)) {
                yield return new CodeInstruction(OpCodes.Ldc_I4, 200);
                continue;
            }

            yield return instruction;
        }
    }

    private static bool LoadsHundred(CodeInstruction instruction) {
        return instruction.operand is sbyte or byte or short or int
            && Convert.ToInt64(instruction.operand, CultureInfo.InvariantCulture) == 100L;
    }
}

[Collection("HarmonySerial")]
public sealed class PositionCoverageTests {
    [Fact]
    public void AnInvokeInjectionAtTheArgumentShift_RunsUnderHarmonyRouting() {
        List<string> log = [];
        MethodInfo target = Method(typeof(PositionCoverageTargets), nameof(PositionCoverageTargets.CallsStep));
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony("test.positioncoverage.invoke.foreign");
        IBackendHandle? handle = null;

        try {
            MonoModEntryPoint.Install(log.Add, null);
            foreign.Patch(target, postfix: new HarmonyMethod(Method(typeof(PositionCoverageMods), nameof(PositionCoverageMods.ForeignNoop))));

            Injection injection = new Injection(
                Method(typeof(PositionCoverageMods), nameof(PositionCoverageMods.ClampArg)),
                new InjectAt.Invoke(typeof(PositionCoverageTargets), nameof(PositionCoverageTargets.Step), At.Argument, 0, null, 1),
                "test.positioncoverage.invoke",
                0);

            handle = PatchBackend.Current.ApplyComposed(target, new[] { injection });
            Applied(handle, log);
            AssertRouted(target);

            PositionCoverageTargets.Ran.Clear();
            Assert.Equal(12, PositionCoverageTargets.CallsStep());
            Assert.Equal(new[] { "arg:1", "foreign" }, PositionCoverageTargets.Ran.ToArray());
        } finally {
            handle?.Dispose();
            TestUnpatch.Own(foreign, "test.positioncoverage.invoke.foreign");
            MonoModEntryPoint.ResetForTests();
        }
    }

    [Fact]
    public void ANewObjInjection_RunsUnderHarmonyRouting() {
        List<string> log = [];
        MethodInfo target = Method(typeof(PositionCoverageTargets), nameof(PositionCoverageTargets.MakesSubject));
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony("test.positioncoverage.newobj.foreign");
        IBackendHandle? handle = null;

        try {
            MonoModEntryPoint.Install(log.Add, null);
            foreign.Patch(target, postfix: new HarmonyMethod(Method(typeof(PositionCoverageMods), nameof(PositionCoverageMods.ForeignNoop))));

            Injection injection = new Injection(
                Method(typeof(PositionCoverageMods), nameof(PositionCoverageMods.Note)),
                new InjectAt.NewObj(typeof(PositionCoverageSubject), At.Head),
                "test.positioncoverage.newobj",
                0);

            handle = PatchBackend.Current.ApplyComposed(target, new[] { injection });
            Applied(handle, log);
            AssertRouted(target);

            PositionCoverageTargets.Ran.Clear();
            Assert.Equal(3, PositionCoverageTargets.MakesSubject());
            Assert.Equal(new[] { "note", "foreign" }, PositionCoverageTargets.Ran.ToArray());
        } finally {
            handle?.Dispose();
            TestUnpatch.Own(foreign, "test.positioncoverage.newobj.foreign");
            MonoModEntryPoint.ResetForTests();
        }
    }

    [Fact]
    public void ALocalInjectionByName_RunsUnderHarmonyRouting() {
        List<string> log = [];
        MethodInfo target = Method(typeof(PositionCoverageTargets), nameof(PositionCoverageTargets.ReadsNamedLocal));
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony("test.positioncoverage.local.foreign");
        IBackendHandle? handle = null;

        try {
            MonoModEntryPoint.Install(log.Add, null);
            foreign.Patch(target, postfix: new HarmonyMethod(Method(typeof(PositionCoverageMods), nameof(PositionCoverageMods.ForeignNoop))));

            Injection injection = new Injection(
                Method(typeof(PositionCoverageMods), nameof(PositionCoverageMods.LogHeld)),
                new InjectAt.Local(typeof(int), LocalAccess.Load, Name: "held"),
                "test.positioncoverage.local",
                0);

            handle = PatchBackend.Current.ApplyComposed(target, new[] { injection });
            Applied(handle, log);
            AssertRouted(target);

            PositionCoverageTargets.Ran.Clear();
            Assert.Equal(106, PositionCoverageTargets.ReadsNamedLocal());
            Assert.Equal(new[] { "read:5", "foreign" }, PositionCoverageTargets.Ran.ToArray());
        } finally {
            handle?.Dispose();
            TestUnpatch.Own(foreign, "test.positioncoverage.local.foreign");
            MonoModEntryPoint.ResetForTests();
        }
    }

    [Fact]
    public void ATranspilerFinal_RunsUnderHarmonyRouting() {
        List<string> log = [];
        MethodInfo target = Method(typeof(PositionCoverageTargets), nameof(PositionCoverageTargets.RetunedFinal));
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony("test.positioncoverage.transpilerfinal.foreign");
        IBackendHandle? handle = null;
        PositionCoverageTargets.Ran.Clear();

        try {
            MonoModEntryPoint.Install(log.Add, null);
            foreign.Patch(target, postfix: new HarmonyMethod(Method(typeof(PositionCoverageMods), nameof(PositionCoverageMods.ForeignNoop))));

            Injection injection = new Injection(
                Method(typeof(PositionCoverageMods), nameof(PositionCoverageMods.Retune)),
                new InjectAt.Transpiler(true),
                "test.positioncoverage.transpilerfinal",
                0);

            handle = PatchBackend.Current.ApplyComposed(target, new[] { injection });
            Applied(handle, log);
            AssertRouted(target);

            Assert.Equal(200, PositionCoverageTargets.RetunedFinal());
            Assert.Equal(new[] { "transpiler", "foreign" }, PositionCoverageTargets.Ran.ToArray());
        } finally {
            handle?.Dispose();
            TestUnpatch.Own(foreign, "test.positioncoverage.transpilerfinal.foreign");
            MonoModEntryPoint.ResetForTests();
        }
    }

    private static MethodInfo Method(Type type, string name) {
        return type.GetMethod(name) ?? throw new InvalidOperationException(type.Name + "." + name + " not found");
    }

    private static void AssertRouted(MethodInfo target) {
        RoutingBackend router = Assert.IsType<RoutingBackend>(PatchBackend.Current);
        Assert.Equal(RouteState.HarmonyOwned, router.GetRoute(target));
    }

    private static void Applied(IBackendHandle handle, List<string> log) {
        Assert.True(
            handle.IsApplied,
            "the handle reports IsApplied false, so this position never reached the brrainz path and the " +
            "assertions below would pass against the unpatched body. " + Lines(log));
    }

    private static string Lines(List<string> log) {
        return log.Count == 0 ? "Nothing was logged." : "Logged: " + string.Join(" | ", log);
    }
}
