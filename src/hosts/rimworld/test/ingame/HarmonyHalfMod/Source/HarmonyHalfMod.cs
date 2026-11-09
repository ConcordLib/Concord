using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Concord.RimWorld.CoexTest.HarmonyHalf;

[StaticConstructorOnStartup]
public static class HarmonyHalfPatches {
    static HarmonyHalfPatches() {
        Type sharedTarget = Type.GetType("Concord.RimWorld.CoexTest.SharedTarget, CoexConcordHalf", true);
        MethodInfo compute = sharedTarget.GetMethod("Compute", BindingFlags.Public | BindingFlags.Static);
        new Harmony("concordtest.harmonyhalf").Patch(compute, postfix: new HarmonyMethod(typeof(HarmonyHalfPatches).GetMethod(nameof(ComputePostfix), BindingFlags.NonPublic | BindingFlags.Static)));
        Log.Message("[COEX] harmony-half-patched");
    }

    private static void ComputePostfix() {
        Log.Message("[COEX] harmony-postfix");
    }
}
