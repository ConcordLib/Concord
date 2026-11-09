using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Backend;
using Concord.MonoMod;
using Concord.MonoMod.Harmony;
using Xunit;

namespace Concord.MonoMod.Tests;

public sealed class ReflectionOnlyCopyTests {
    private static Assembly? reflectionOnlyCopy;

    [Fact]
    public void AReflectionOnlyHarmonyCopy_IsNotArmed_AndLogsOneLine() {
        List<string> log = [];
        Assembly copy = ReflectionOnlyHarmony();

        try {
            Assert.False(
                UpdateWrapperHook.TryInstallFor(copy, new MonoModBackend(), log.Add),
                "a reflection-only Harmony copy was armed. Logged: " + string.Join(" | ", log));

            Assert.False(UpdateWrapperHook.IsHookedOn(copy));
            Assert.Single(
                log,
                line => line.StartsWith(CoexistenceLogMarkers.HookSkipped, StringComparison.Ordinal)
                        && line.Contains("#" + copy.GetHashCode())
                        && line.Contains("reflection-only"));
            Assert.DoesNotContain(
                log,
                line => line.StartsWith(CoexistenceLogMarkers.HookUnavailable, StringComparison.Ordinal));
        } finally {
            Disarm();
        }
    }

    [Fact]
    public void AReflectionOnlyHarmonyCopy_LeavesTheUnpatchedReadAnswering() {
        List<string> log = [];
        ReflectionOnlyHarmony();
        MethodInfo target = typeof(ReflectionOnlyTargets).GetMethod(nameof(ReflectionOnlyTargets.Unpatched))!;

        try {
            HarmonyBridge bridge = Arm(log);

            Assert.False(
                bridge.HasPatch(target),
                "the patch read found a patch nobody made. Logged: " + string.Join(" | ", log));
        } finally {
            Disarm();
        }
    }

    [Fact]
    public void ANormalInstall_IsNotDead() {
        List<string> log = [];
        MonoModEntryPoint.ResetForTests();

        try {
            Selection chosen = MonoModEntryPoint.Install(log.Add, null);

            Assert.True(chosen.Installed, string.Join("\n", log));
            Assert.False(chosen.Dead, string.Join("\n", log));
        } finally {
            MonoModEntryPoint.ResetForTests();
        }
    }

    private static Assembly ReflectionOnlyHarmony() {
        if (reflectionOnlyCopy != null) {
            return reflectionOnlyCopy;
        }

        byte[] image = File.ReadAllBytes(typeof(HarmonyLib.Harmony).Assembly.Location);
        Assembly copy = Assembly.ReflectionOnlyLoad(AssemblyImage.WithFreshModuleId(image));
        Assert.True(copy.ReflectionOnly, "the second copy of Harmony did not load reflection-only");
        Assert.NotNull(copy.GetType("HarmonyLib.Harmony"));
        reflectionOnlyCopy = copy;
        return copy;
    }

    private static HarmonyBridge Arm(List<string> log) {
        UpdateWrapperHook.ResetForTests();

        HarmonyBridge bridge = new HarmonyBridge(log.Add);
        RoutingBackend router = new RoutingBackend(new MonoModBackend(), log.Add);
        router.ActivateHost(bridge);
        Assert.True(router.NotifierInstalled, "the notifier did not install. Logged: " + string.Join(" | ", log));
        return bridge;
    }

    private static void Disarm() {
        UpdateWrapperHook.ResetForTests();
        UpdateWrapperHook.DetachObserver();
        MonoModEntryPoint.ResetForTests();
    }

    public static class ReflectionOnlyTargets {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Unpatched(int seed) {
            return seed + 1;
        }
    }
}
