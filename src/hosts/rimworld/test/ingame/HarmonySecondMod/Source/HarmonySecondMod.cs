using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Concord.RimWorld.CoexTest.HarmonySecond;

public class HarmonySecondMod : Mod {
    private static Type sharedTargetType;
    private static MethodInfo computeMethod;

    public HarmonySecondMod(ModContentPack content) : base(content) {
        LongEventHandler.ExecuteWhenFinished(() => LongEventHandler.ExecuteWhenFinished(() => LongEventHandler.ExecuteWhenFinished(() => {
            sharedTargetType = Type.GetType("Concord.RimWorld.CoexTest.SharedTarget, CoexConcordHalf", true);
            computeMethod = sharedTargetType.GetMethod("Compute", BindingFlags.Public | BindingFlags.Static);
            new Harmony("concordtest.harmonysecond").Patch(computeMethod, postfix: new HarmonyMethod(typeof(HarmonySecondMod).GetMethod(nameof(ComputePostfix), BindingFlags.NonPublic | BindingFlags.Static)));
            Log.Message("[COEX] harmony-second-patched");

            LongEventHandler.ExecuteWhenFinished(() => {
                computeMethod.Invoke(null, new object[] { 41 });
                FieldInfo headCountField = sharedTargetType.GetField("HeadCount", BindingFlags.Public | BindingFlags.Static);
                object headCount = headCountField.GetValue(null);
                Log.Message("[COEX] second-invoke head-count=" + headCount);
            });
        })));
    }

    private static void ComputePostfix() {
        Log.Message("[COEX] harmony-second-postfix");
    }
}
