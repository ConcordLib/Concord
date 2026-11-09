using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace Concord.RimWorld.CoexTest.HarmonyBreaker;

public class HarmonyBreakerMod : Mod {
    private static Type sharedTargetType;
    private static MethodInfo computeMethod;

    public HarmonyBreakerMod(ModContentPack content) : base(content) {
        LongEventHandler.ExecuteWhenFinished(() => LongEventHandler.ExecuteWhenFinished(() => LongEventHandler.ExecuteWhenFinished(() => {
            sharedTargetType = Type.GetType("Concord.RimWorld.CoexTest.SharedTarget, CoexConcordHalf", true);
            computeMethod = sharedTargetType.GetMethod("Compute", BindingFlags.Public | BindingFlags.Static);
            new Harmony("concordtest.harmonybreaker").Patch(computeMethod, transpiler: new HarmonyMethod(typeof(HarmonyBreakerMod).GetMethod(nameof(FaultWrapTranspiler), BindingFlags.NonPublic | BindingFlags.Static)));
            Log.Message("[COEX] harmony-breaker-patched");

            LongEventHandler.ExecuteWhenFinished(() => {
                object result = computeMethod.Invoke(null, new object[] { 41 });
                FieldInfo headCountField = sharedTargetType.GetField("HeadCount", BindingFlags.Public | BindingFlags.Static);
                object headCount = headCountField.GetValue(null);
                Log.Message("[COEX] breaker-invoke result=" + result + " head-count=" + headCount);
            });
        })));
    }

    private static IEnumerable<CodeInstruction> FaultWrapTranspiler(IEnumerable<CodeInstruction> instructions) {
        List<CodeInstruction> stream = new List<CodeInstruction>(instructions);
        if (stream.Count == 0) {
            return stream;
        }

        stream[0].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));

        int retIndex = stream.FindIndex(ins => ins.opcode == OpCodes.Ret);
        int endIndex = retIndex >= 0 ? retIndex : stream.Count - 1;
        stream[endIndex].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginFaultBlock));
        stream[endIndex].blocks.Add(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));

        return stream;
    }
}
