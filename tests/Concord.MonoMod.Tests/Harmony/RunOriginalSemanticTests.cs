using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord;
using Concord.Backend;
using Concord.Emit;
using Concord.MonoMod;
using HarmonyLib;
using Xunit;

namespace Concord.MonoMod.Tests;

public static class RunOriginalSemanticTargets {
    public static readonly List<string> Ran = [];

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Skipped(int seed) {
        Ran.Add("body");
        return seed + 1;
    }
}

public static class RunOriginalSemanticMods {
    public static void ConcordNote() {
        RunOriginalSemanticTargets.Ran.Add("concord");
    }

    public static bool HarmonyCancels() {
        RunOriginalSemanticTargets.Ran.Add("harmony");
        return false;
    }
}

[Collection("HarmonySerial")]
public sealed class RunOriginalSemanticTests {
    [Fact]
    public void AHarmonyPrefixThatSkipsTheOriginal_SkipsConcordsBodyToo() {
        List<string> log = [];
        MethodInfo target = typeof(RunOriginalSemanticTargets).GetMethod(nameof(RunOriginalSemanticTargets.Skipped))!;
        const string id = "test.runoriginal.semantic";
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony(id);
        IBackendHandle? handle = null;

        try {
            MonoModEntryPoint.Install(log.Add, null);
            handle = PatchBackend.Current.ApplyComposed(target, new[] {
                new Injection(
                    typeof(RunOriginalSemanticMods).GetMethod(nameof(RunOriginalSemanticMods.ConcordNote))!,
                    new InjectAt.Head(),
                    "test.runoriginal.concord",
                    0),
            });

            foreign.Patch(
                target,
                prefix: new HarmonyMethod(typeof(RunOriginalSemanticMods).GetMethod(nameof(RunOriginalSemanticMods.HarmonyCancels))!));

            RunOriginalSemanticTargets.Ran.Clear();
            RunOriginalSemanticTargets.Skipped(1);

            Assert.Equal(new[] { "harmony" }, RunOriginalSemanticTargets.Ran.ToArray());
        } finally {
            handle?.Dispose();
            TestUnpatch.Own(foreign, id);
            MonoModEntryPoint.ResetForTests();
        }
    }
}
