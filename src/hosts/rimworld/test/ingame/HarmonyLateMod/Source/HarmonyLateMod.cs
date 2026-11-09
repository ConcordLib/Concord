using System;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Verse;

namespace Concord.RimWorld.CoexTest.HarmonyLate;

public class HarmonyLateMod : Mod {
    public HarmonyLateMod(ModContentPack content) : base(content) {
        LongEventHandler.ExecuteWhenFinished(() => LongEventHandler.ExecuteWhenFinished(StartTimer));
    }

    private static void StartTimer() {
        Thread timer = new Thread(() => {
            Thread.Sleep(5000);
            LongEventHandler.ExecuteWhenFinished(PatchAndInvoke);
        });

        timer.IsBackground = true;
        timer.Start();
    }

    private static void PatchAndInvoke() {
        Type sharedTarget = Type.GetType("Concord.RimWorld.CoexTest.SharedTarget, CoexConcordHalf", true);
        MethodInfo compute = sharedTarget.GetMethod("Compute", BindingFlags.Public | BindingFlags.Static);
        new Harmony("concordtest.harmonylate").Patch(compute, postfix: new HarmonyMethod(typeof(HarmonyLateMod).GetMethod(nameof(ComputePostfix), BindingFlags.NonPublic | BindingFlags.Static)));
        Log.Message("[COEX] harmony-late-patched");

        object result = compute.Invoke(null, new object[] { 41 });
        FieldInfo headCount = sharedTarget.GetField("HeadCount", BindingFlags.Public | BindingFlags.Static);
        Log.Message("[COEX] late-invoke result=" + result + " head-count=" + headCount.GetValue(null));
    }

    private static void ComputePostfix() {
        Log.Message("[COEX] harmony-postfix");
    }
}
