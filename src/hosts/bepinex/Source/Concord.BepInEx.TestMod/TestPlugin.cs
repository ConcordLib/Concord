using System.Collections;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Concord.BepInEx.TestMod;

[BepInPlugin("concordlib.concord.testmod", "Concord TestMod", "0.0.0")]
[BepInDependency("concordlib.concord")]
public sealed class TestPlugin : BaseUnityPlugin {
    private static ManualLogSource log;
    private readonly HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("concordlib.concord.testmod.harmony");

    public static void HarmonyPrefix() {
        Mark("harmony-prefix");
    }

    public static void Mark(string marker) {
        log.LogInfo("[Concord.TestMod] " + marker);
    }

    private void Awake() {
        log = Logger;
        harmony.Patch(typeof(ProbeTargets).GetMethod(nameof(ProbeTargets.Contested)), prefix: new HarmonyMethod(typeof(TestPlugin).GetMethod(nameof(HarmonyPrefix))));
        Patcher.Apply(typeof(TestPlugin).Assembly);

        ProbeTargets.Clean();
        ProbeTargets.Contested();
        StartCoroutine(LateSteps());
    }

    private IEnumerator LateSteps() {
        yield return new WaitForEndOfFrame();
        harmony.Patch(typeof(ProbeTargets).GetMethod(nameof(ProbeTargets.Late)), prefix: new HarmonyMethod(typeof(TestPlugin).GetMethod(nameof(HarmonyPrefix))));
        ProbeTargets.Late();
        yield return new WaitForEndOfFrame();
        harmony.Unpatch(typeof(ProbeTargets).GetMethod(nameof(ProbeTargets.Late)), typeof(TestPlugin).GetMethod(nameof(HarmonyPrefix)));
        ProbeTargets.Late();
        Mark("probe-unpatched");
    }
}
