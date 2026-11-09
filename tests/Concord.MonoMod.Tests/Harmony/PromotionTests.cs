using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord;
using Concord.Backend;
using Concord.Emit;
using Concord.MonoMod;
using Concord.MonoMod.Harmony;
using HarmonyLib;
using Xunit;

namespace Concord.MonoMod.Tests;

// Serialized with the rest of the Harmony tests: the notifier hook and Harmony's own patch state are
// process-wide, so these cannot run alongside another test that patches.
[Collection("HarmonySerial")]
public class PromotionTests
{
    [Fact]
    public void ForeignPatchAfterConcordDetour_PromotesToBridge_AndBothRun()
    {
        PromotionLog.Entries.Clear();
        MethodBase target = Method(typeof(PromotionTarget), nameof(PromotionTarget.Compute));
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony("test.foreign.promotion");

        RoutingBackend router = new RoutingBackend(new MonoModBackend(), Noop);
        HarmonyBridge bridge = new HarmonyBridge(Noop);

        try
        {
            router.ActivateHost(bridge);
            Assert.True(router.NotifierInstalled);

            using IBackendHandle handle = router.ApplyComposed(target, new[] { HeadOf(nameof(PromotionMods.ConcordHead)) });

            // Nothing foreign has touched it yet, so Concord owns the entry point outright.
            Assert.Equal(RouteState.Raw, router.GetRoute(target));
            Assert.Equal(1, PromotionTarget.Compute());
            Assert.Equal(new[] { "concord" }, PromotionLog.Entries);

            PromotionLog.Entries.Clear();

            // The notifier should fire inside this call and move the method onto the bridge before
            // Harmony finishes building its wrapper.
            foreign.Patch(
                target,
                prefix: new HarmonyMethod(Method(typeof(PromotionMods), nameof(PromotionMods.ForeignPrefix))));

            Assert.Equal(RouteState.HarmonyOwned, router.GetRoute(target));

            Assert.Equal(1, PromotionTarget.Compute());
            Assert.Contains("concord", PromotionLog.Entries);
            Assert.Contains("foreign", PromotionLog.Entries);
        }
        finally
        {
            TestUnpatch.Own(foreign, "test.foreign.promotion");
            UpdateWrapperHook.DetachObserver();
        }
    }

    [Fact]
    public void ForeignUnpatch_LeavesPromotedMethodOnTheBridge()
    {
        PromotionLog.Entries.Clear();
        MethodBase target = Method(typeof(UnpatchTarget), nameof(UnpatchTarget.Compute));
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony("test.foreign.unpatch");

        RoutingBackend router = new RoutingBackend(new MonoModBackend(), Noop);
        HarmonyBridge bridge = new HarmonyBridge(Noop);

        try
        {
            router.ActivateHost(bridge);

            using IBackendHandle handle = router.ApplyComposed(target, new[] { HeadOf(nameof(PromotionMods.UnpatchHead)) });
            Assert.Equal(RouteState.Raw, router.GetRoute(target));

            foreign.Patch(
                target,
                prefix: new HarmonyMethod(Method(typeof(PromotionMods), nameof(PromotionMods.UnpatchForeignPrefix))));
            Assert.Equal(RouteState.HarmonyOwned, router.GetRoute(target));

            TestUnpatch.Own(foreign, "test.foreign.unpatch");

            // Route state is monotonic: once bridged, it stays bridged for the session.
            Assert.Equal(RouteState.HarmonyOwned, router.GetRoute(target));

            PromotionLog.Entries.Clear();
            Assert.Equal(2, UnpatchTarget.Compute());
            Assert.Contains("concord-unpatch", PromotionLog.Entries);
        }
        finally
        {
            TestUnpatch.Own(foreign, "test.foreign.unpatch");
            UpdateWrapperHook.DetachObserver();
        }
    }

    [Fact]
    public void SharedGenericInstantiation_RoutesThroughTheBridgeInsteadOfClobberingHarmony()
    {
        if (TestRuntime.IsNetFramework)
        {
            return;
        }

        PromotionLog.Entries.Clear();
        MethodBase patched = Method(typeof(SharedRouteTarget<string>), nameof(SharedRouteTarget<string>.Compute));
        MethodBase sibling = Method(typeof(SharedRouteTarget<Version>), nameof(SharedRouteTarget<Version>.Compute));
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony("test.foreign.sharedroute");

        RoutingBackend router = new RoutingBackend(new MonoModBackend(), Noop);
        HarmonyBridge bridge = new HarmonyBridge(Noop);

        try
        {
            router.ActivateHost(bridge);

            foreign.Patch(
                patched,
                postfix: new HarmonyMethod(Method(typeof(PromotionMods), nameof(PromotionMods.SharedRouteForeignPostfix))));

            // The sibling instantiation is a different MethodBase but the same native body, so Harmony
            // already owns this entry point and Concord must bridge rather than detour over it.
            using IBackendHandle handle = router.ApplyComposed(sibling, new[] { HeadOf(nameof(PromotionMods.SharedRouteHead)) });
            Assert.Equal(RouteState.HarmonyOwned, router.GetRoute(sibling));

            PromotionLog.Entries.Clear();
            Assert.Equal(4, new SharedRouteTarget<Version>().Compute());
            Assert.Equal(new[] { "concord-shared", "foreign-shared" }, PromotionLog.Entries);

            PromotionLog.Entries.Clear();
            Assert.Equal(4, new SharedRouteTarget<string>().Compute());
            Assert.Equal(new[] { "foreign-shared" }, PromotionLog.Entries);
        }
        finally
        {
            TestUnpatch.Own(foreign, "test.foreign.sharedroute");
            UpdateWrapperHook.DetachObserver();
        }
    }

    [Fact]
    public void ConcordOwnRebuilds_DoNotRecurseThroughTheNotifier()
    {
        MethodBase target = Method(typeof(RecursionTarget), nameof(RecursionTarget.Compute));
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony("test.foreign.recursion");

        MonoModBackend inner = new MonoModBackend();
        RoutingBackend router = new RoutingBackend(inner, Noop);
        HarmonyBridge bridge = new HarmonyBridge(Noop);
        CountingObserver observer = new CountingObserver();
        IBackendHandle? concord = null;

        try
        {
            router.ActivateHost(bridge);
            foreign.Patch(target, prefix: new HarmonyMethod(Method(typeof(PromotionMods), nameof(PromotionMods.RecursionPrefix))));

            Assert.True(UpdateWrapperHook.TryInstall(observer, inner, Noop));

            concord = router.ApplyComposed(target, new[] { HeadOf(nameof(PromotionMods.RecursionHead)) });

            Assert.Equal(RouteState.HarmonyOwned, router.GetRoute(target));
            Assert.Equal(0, observer.Count);
        }
        finally
        {
            concord?.Dispose();
            TestUnpatch.Own(foreign, "test.foreign.recursion");
            UpdateWrapperHook.DetachObserver();
        }
    }

    private static void Noop(string message)
    {
    }

    private static MethodInfo Method(Type type, string name)
    {
        return type.GetMethod(name) ?? throw new InvalidOperationException(type.Name + "." + name + " not found");
    }

    private static Injection HeadOf(string name)
    {
        return new Injection(Method(typeof(PromotionMods), name), new InjectAt.Head(), "test.concord.promotion", 0);
    }

    private sealed class CountingObserver : IForeignPatchObserver
    {
        public int Count { get; private set; }

        public void OnForeignPatchPending(MethodBase target, object hostPatchState)
        {
            Count++;
        }
    }

    public static class PromotionLog
    {
        public static List<string> Entries { get; } = new List<string>();
    }

    public static class PromotionTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            return 1;
        }
    }

    public static class UnpatchTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            return 2;
        }
    }

    public sealed class SharedRouteTarget<T>
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Compute()
        {
            return 4;
        }
    }

    public static class RecursionTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            return 3;
        }
    }

    public static class PromotionMods
    {
        public static void ConcordHead(ControlHandle<int> ch)
        {
            PromotionLog.Entries.Add("concord");
        }

        public static void ForeignPrefix()
        {
            PromotionLog.Entries.Add("foreign");
        }

        public static void UnpatchHead(ControlHandle<int> ch)
        {
            PromotionLog.Entries.Add("concord-unpatch");
        }

        public static void UnpatchForeignPrefix()
        {
            PromotionLog.Entries.Add("foreign-unpatch");
        }

        public static void SharedRouteHead(ControlHandle<int> ch)
        {
            PromotionLog.Entries.Add("concord-shared");
        }

        public static void SharedRouteForeignPostfix()
        {
            PromotionLog.Entries.Add("foreign-shared");
        }

        public static void RecursionPrefix()
        {
        }

        public static void RecursionHead(ControlHandle<int> ch)
        {
        }
    }
}
