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

public static class HarmonyOwnedTargets {
    public static readonly List<string> Ran = [];

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Withdrawn(int seed) {
        Ran.Add("body");
        return seed + 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int LastPatchGone(int seed) {
        Ran.Add("body");
        return seed + 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Reversed(int seed) {
        Ran.Add("body");
        return seed + 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Superseded(int seed) {
        Ran.Add("body");
        return seed + 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int BuildThrows(int seed) {
        Ran.Add("body");
        return seed + 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int BuildSucceeds(int seed) {
        Ran.Add("body");
        return seed + 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int RebuildRefused(int seed) {
        Ran.Add("body");
        return seed + 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int RebuildAccepted(int seed) {
        Ran.Add("body");
        return seed + 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int RefusedAdd(int seed) {
        Ran.Add("body");
        return seed + 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int RefusedDispose(int seed) {
        Ran.Add("body");
        return seed + 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int RefusedRecompose(int seed) {
        Ran.Add("body");
        return seed + 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ForeignOnly(int seed) {
        Ran.Add("body");
        return seed + 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ForeignAndConcord(int seed) {
        Ran.Add("body");
        return seed + 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int QuietWithdraw(int seed) {
        Ran.Add("body");
        return seed + 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int QuietWithdrawRoute(int seed) {
        Ran.Add("body");
        return seed + 1;
    }
}

public static class HarmonyOwnedMods {
    public static void ConcordNote() {
        HarmonyOwnedTargets.Ran.Add("concord");
    }

    public static void ConcordSecondNote() {
        HarmonyOwnedTargets.Ran.Add("concord-two");
    }

    public static void ConcordThirdNote() {
        HarmonyOwnedTargets.Ran.Add("concord-three");
    }

    public static void HarmonyPrefix() {
        HarmonyOwnedTargets.Ran.Add("harmony");
    }

    public static void RunOriginalOff(ref bool __runOriginal) {
        HarmonyOwnedTargets.Ran.Add("harmony");
        __runOriginal = false;
    }
}

public static class HarmonyBuildTranspilers {
    public static bool SupersededDuringBuild { get; set; }

    public static bool AppliedDuringBuild { get; set; }

    public static IBackendHandle? Watched { get; set; }

    public static int Ran { get; set; }

    public static IEnumerable<HarmonyLib.CodeInstruction> Throw(IEnumerable<HarmonyLib.CodeInstruction> instructions) {
        Ran++;
        AppliedDuringBuild = Watched!.IsApplied;
        throw new InvalidOperationException("this transpiler fails every wrapper build it is given");
    }

    public static IEnumerable<HarmonyLib.CodeInstruction> WatchSupersede(IEnumerable<HarmonyLib.CodeInstruction> instructions) {
        Ran++;
        SupersededDuringBuild = TargetDetourRegistry.IsSuperseded(
            typeof(HarmonyOwnedTargets).GetMethod(nameof(HarmonyOwnedTargets.BuildSucceeds))!);
        return new List<HarmonyLib.CodeInstruction>(instructions);
    }
}

public static class RefusingRebuildTranspiler {
    public static bool Refuse { get; set; }

    public static IEnumerable<HarmonyLib.CodeInstruction> Maybe(IEnumerable<HarmonyLib.CodeInstruction> instructions) {
        if (Refuse) {
            throw new InvalidOperationException("this transpiler fails every wrapper rebuild it is asked for");
        }

        return new List<HarmonyLib.CodeInstruction>(instructions);
    }
}

public static class ReverseStandin {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Call(int seed) {
        throw new NotImplementedException("the reverse patch replaces this body");
    }
}

[Collection("HarmonySerial")]
public sealed class HarmonyOwnedStateTests {
    [Fact]
    public void ConcordWithdrawsOnAHarmonyOwnedMethod_HarmonyRebuildsWithoutIt_AndHarmonysOwnPatchStillRuns() {
        List<string> log = [];
        MethodInfo target = Method(nameof(HarmonyOwnedTargets.Withdrawn));
        const string id = "test.harmonyowned.withdraw";
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony(id);

        try {
            MonoModEntryPoint.Install(log.Add, null);
            foreign.Patch(target, prefix: new HarmonyMethod(Mod(nameof(HarmonyOwnedMods.HarmonyPrefix))));

            IBackendHandle handle = PatchBackend.Current.ApplyComposed(target, new[] { Note() });

            HarmonyOwnedTargets.Ran.Clear();
            Assert.Equal(2, HarmonyOwnedTargets.Withdrawn(1));
            Assert.Contains("concord", HarmonyOwnedTargets.Ran);
            Assert.Contains("harmony", HarmonyOwnedTargets.Ran);

            handle.Dispose();

            HarmonyOwnedTargets.Ran.Clear();
            Assert.Equal(2, HarmonyOwnedTargets.Withdrawn(1));
            Assert.DoesNotContain("concord", HarmonyOwnedTargets.Ran);
            Assert.Contains("harmony", HarmonyOwnedTargets.Ran);
            Assert.Contains("body", HarmonyOwnedTargets.Ran);
        } finally {
            TestUnpatch.Own(foreign, id);
            MonoModEntryPoint.ResetForTests();
        }
    }

    [Fact]
    public void ConcordWithdrawsOnAPatchTheNotifierNeverSaw_HarmonysPrefixKeepsRunning() {
        List<string> log = [];
        MethodInfo target = Method(nameof(HarmonyOwnedTargets.QuietWithdraw));
        const string id = "test.harmonyowned.quietwithdraw";
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony(id);

        try {
            MonoModEntryPoint.Install(log.Add, null);
            IBackendHandle handle = PatchBackend.Current.ApplyComposed(target, new[] { Note() });

            using (UpdateWrapperHook.Suppress()) {
                foreign.Patch(target, prefix: new HarmonyMethod(Mod(nameof(HarmonyOwnedMods.HarmonyPrefix))));
            }

            handle.Dispose();

            HarmonyOwnedTargets.Ran.Clear();
            Assert.Equal(2, HarmonyOwnedTargets.QuietWithdraw(1));
            Assert.DoesNotContain("concord", HarmonyOwnedTargets.Ran);
            Assert.Contains("harmony", HarmonyOwnedTargets.Ran);
            Assert.Contains("body", HarmonyOwnedTargets.Ran);
        } finally {
            TestUnpatch.Own(foreign, id);
            MonoModEntryPoint.ResetForTests();
        }
    }

    [Fact]
    public void ConcordWithdrawsOnAPatchTheNotifierNeverSaw_RouteReadsHarmonyOwned() {
        List<string> log = [];
        MethodInfo target = Method(nameof(HarmonyOwnedTargets.QuietWithdrawRoute));
        const string id = "test.harmonyowned.quietwithdrawroute";
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony(id);

        try {
            MonoModEntryPoint.Install(log.Add, null);
            RoutingBackend router = (RoutingBackend)PatchBackend.Current;
            IBackendHandle handle = PatchBackend.Current.ApplyComposed(target, new[] { Note() });

            using (UpdateWrapperHook.Suppress()) {
                foreign.Patch(target, prefix: new HarmonyMethod(Mod(nameof(HarmonyOwnedMods.HarmonyPrefix))));
            }

            handle.Dispose();

            Assert.Equal(RouteState.HarmonyOwned, router.GetRoute(target));
        } finally {
            TestUnpatch.Own(foreign, id);
            MonoModEntryPoint.ResetForTests();
        }
    }

    [Fact]
    public void HarmonyRemovesItsLastPatch_AndConcordStillRuns() {
        List<string> log = [];
        MethodInfo target = Method(nameof(HarmonyOwnedTargets.LastPatchGone));
        const string id = "test.harmonyowned.lastpatch";
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony(id);
        IBackendHandle? handle = null;

        try {
            MonoModEntryPoint.Install(log.Add, null);
            handle = PatchBackend.Current.ApplyComposed(target, new[] { Note() });
            foreign.Patch(target, prefix: new HarmonyMethod(Mod(nameof(HarmonyOwnedMods.HarmonyPrefix))));

            TestUnpatch.Own(foreign, id);

            HarmonyOwnedTargets.Ran.Clear();
            Assert.Equal(2, HarmonyOwnedTargets.LastPatchGone(1));
            Assert.Contains("concord", HarmonyOwnedTargets.Ran);
            Assert.Contains("body", HarmonyOwnedTargets.Ran);
            Assert.DoesNotContain("harmony", HarmonyOwnedTargets.Ran);
        } finally {
            handle?.Dispose();
            TestUnpatch.Own(foreign, id);
            MonoModEntryPoint.ResetForTests();
        }
    }

    [Fact]
    public void AReversePatchOfAConcordOwnedMethod_SeesConcordsBody() {
        List<string> log = [];
        MethodInfo target = Method(nameof(HarmonyOwnedTargets.Reversed));
        const string id = "test.harmonyowned.reverse";
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony(id);
        IBackendHandle? handle = null;

        try {
            MonoModEntryPoint.Install(log.Add, null);
            handle = PatchBackend.Current.ApplyComposed(target, new[] { Note() });

            HarmonyMethod standin = new HarmonyMethod(typeof(ReverseStandin).GetMethod(nameof(ReverseStandin.Call))!);
            new HarmonyLib.Harmony(id).CreateReversePatcher(target, standin).Patch();

            HarmonyOwnedTargets.Ran.Clear();
            Assert.Equal(2, ReverseStandin.Call(1));
            Assert.Contains("concord", HarmonyOwnedTargets.Ran);
            Assert.Contains("body", HarmonyOwnedTargets.Ran);
        } finally {
            handle?.Dispose();
            TestUnpatch.Own(foreign, id);
            MonoModEntryPoint.ResetForTests();
        }
    }

    [Fact]
    public void ASupersededRawHandle_ReleasedAfterHarmonysWrapperLands_LeavesTheWrapperRunning() {
        List<string> log = [];
        MethodInfo target = Method(nameof(HarmonyOwnedTargets.Superseded));
        const string id = "test.harmonyowned.superseded";
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony(id);

        try {
            MonoModEntryPoint.Install(log.Add, null);
            IBackendHandle raw = PatchBackend.Current.ApplyComposed(target, new[] { Note() });

            foreign.Patch(target, prefix: new HarmonyMethod(Mod(nameof(HarmonyOwnedMods.HarmonyPrefix))));

            raw.Dispose();

            HarmonyOwnedTargets.Ran.Clear();
            Assert.Equal(2, HarmonyOwnedTargets.Superseded(1));
            Assert.Contains("harmony", HarmonyOwnedTargets.Ran);
            Assert.Contains("body", HarmonyOwnedTargets.Ran);
        } finally {
            TestUnpatch.Own(foreign, id);
            MonoModEntryPoint.ResetForTests();
        }
    }

    [Fact]
    public void HarmonysWrapperBuildThrows_TheMethodStaysRaw_AndConcordKeepsComposingIt() {
        List<string> log = [];
        MethodInfo target = Method(nameof(HarmonyOwnedTargets.BuildThrows));
        const string id = "test.harmonyowned.buildthrows";
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony(id);
        IBackendHandle? first = null;
        IBackendHandle? second = null;

        try {
            MonoModEntryPoint.Install(log.Add, null);
            RoutingBackend router = (RoutingBackend)PatchBackend.Current;
            first = PatchBackend.Current.ApplyComposed(target, new[] { Note() });

            Assert.True(first.IsApplied);
            Assert.Equal(RouteState.Raw, router.GetRoute(target));

            HarmonyBuildTranspilers.Ran = 0;
            HarmonyBuildTranspilers.AppliedDuringBuild = false;
            HarmonyBuildTranspilers.Watched = first;
            Assert.ThrowsAny<Exception>(
                () => foreign.Patch(target, transpiler: new HarmonyMethod(Transpiler(nameof(HarmonyBuildTranspilers.Throw)))));
            Assert.Equal(1, HarmonyBuildTranspilers.Ran);
            Assert.True(HarmonyBuildTranspilers.AppliedDuringBuild);

            Assert.Equal(RouteState.Raw, router.GetRoute(target));
            Assert.True(first.IsApplied);

            HarmonyOwnedTargets.Ran.Clear();
            Assert.Equal(2, HarmonyOwnedTargets.BuildThrows(1));
            Assert.Contains("concord", HarmonyOwnedTargets.Ran);
            Assert.Contains("body", HarmonyOwnedTargets.Ran);

            second = PatchBackend.Current.ApplyComposed(target, new[] { SecondNote() });

            Assert.True(second.IsApplied);
            HarmonyOwnedTargets.Ran.Clear();
            Assert.Equal(2, HarmonyOwnedTargets.BuildThrows(1));
            Assert.Contains("concord", HarmonyOwnedTargets.Ran);
            Assert.Contains("concord-two", HarmonyOwnedTargets.Ran);
            Assert.Contains("body", HarmonyOwnedTargets.Ran);
        } finally {
            HarmonyBuildTranspilers.Watched = null;
            second?.Dispose();
            first?.Dispose();
            TestUnpatch.Own(foreign, id);
            MonoModEntryPoint.ResetForTests();
        }
    }

    [Fact]
    public void HarmonysWrapperBuildSucceeds_TheMethodBecomesHarmonyOwned_AndTheRawHandleIsSupersededOnlyAfterTheReturn() {
        List<string> log = [];
        MethodInfo target = Method(nameof(HarmonyOwnedTargets.BuildSucceeds));
        const string id = "test.harmonyowned.buildsucceeds";
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony(id);
        IBackendHandle? handle = null;

        try {
            MonoModEntryPoint.Install(log.Add, null);
            RoutingBackend router = (RoutingBackend)PatchBackend.Current;
            handle = PatchBackend.Current.ApplyComposed(target, new[] { Note() });

            Assert.Equal(RouteState.Raw, router.GetRoute(target));
            Assert.False(TargetDetourRegistry.IsSuperseded(target));

            HarmonyBuildTranspilers.Ran = 0;
            HarmonyBuildTranspilers.SupersededDuringBuild = true;
            foreign.Patch(
                target,
                prefix: new HarmonyMethod(Mod(nameof(HarmonyOwnedMods.HarmonyPrefix))),
                transpiler: new HarmonyMethod(Transpiler(nameof(HarmonyBuildTranspilers.WatchSupersede))));

            Assert.Equal(1, HarmonyBuildTranspilers.Ran);
            Assert.False(HarmonyBuildTranspilers.SupersededDuringBuild);
            Assert.Equal(RouteState.HarmonyOwned, router.GetRoute(target));
            Assert.True(TargetDetourRegistry.IsSuperseded(target));
            Assert.True(handle.IsApplied);

            HarmonyOwnedTargets.Ran.Clear();
            Assert.Equal(2, HarmonyOwnedTargets.BuildSucceeds(1));
            Assert.Contains("concord", HarmonyOwnedTargets.Ran);
            Assert.Contains("harmony", HarmonyOwnedTargets.Ran);
            Assert.Contains("body", HarmonyOwnedTargets.Ran);
        } finally {
            handle?.Dispose();
            TestUnpatch.Own(foreign, id);
            MonoModEntryPoint.ResetForTests();
        }
    }

    [Fact]
    public void HarmonyRefusesTheRebuildForAnAddedInjection_OnlyThatInjectionIsUndone_AndTheWrapperKeepsRunning() {
        List<string> log = [];
        MethodInfo target = Method(nameof(HarmonyOwnedTargets.RefusedAdd));
        const string id = "test.harmonyowned.refusedadd";
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony(id);
        IBackendHandle? first = null;

        try {
            MonoModEntryPoint.Install(log.Add, null);
            RoutingBackend router = (RoutingBackend)PatchBackend.Current;
            first = PatchBackend.Current.ApplyComposed(target, new[] { Note() });
            ArmRefusableHarmony(foreign, target);
            Assert.Equal(RouteState.HarmonyOwned, router.GetRoute(target));

            RefusingRebuildTranspiler.Refuse = true;
            ConcordEmitException failure = Assert.Throws<ConcordEmitException>(
                () => PatchBackend.Current.ApplyComposed(target, new[] { SecondNote() }));

            Assert.Contains(SoleHarmonyRefusalReason(log), failure.Message, StringComparison.Ordinal);
            Assert.True(first.IsApplied);
            Assert.Equal(RouteState.HarmonyOwned, router.GetRoute(target));

            HarmonyOwnedTargets.Ran.Clear();
            Assert.Equal(2, HarmonyOwnedTargets.RefusedAdd(1));
            Assert.Contains("harmony", HarmonyOwnedTargets.Ran);
            Assert.Contains("concord", HarmonyOwnedTargets.Ran);
            Assert.Contains("body", HarmonyOwnedTargets.Ran);
            Assert.DoesNotContain("concord-two", HarmonyOwnedTargets.Ran);
        } finally {
            RefusingRebuildTranspiler.Refuse = false;
            first?.Dispose();
            TestUnpatch.Own(foreign, id);
            MonoModEntryPoint.ResetForTests();
        }
    }

    [Fact]
    public void HarmonyRefusesTheRebuildForAWithdrawal_TheHandleStaysApplied_AndASecondWithdrawalSucceeds() {
        List<string> log = [];
        MethodInfo target = Method(nameof(HarmonyOwnedTargets.RefusedDispose));
        const string id = "test.harmonyowned.refuseddispose";
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony(id);
        IBackendHandle? handle = null;

        try {
            MonoModEntryPoint.Install(log.Add, null);
            handle = PatchBackend.Current.ApplyComposed(target, new[] { Note() });
            ArmRefusableHarmony(foreign, target);

            RefusingRebuildTranspiler.Refuse = true;
            IBackendHandle refused = handle;
            ConcordEmitException failure = Assert.Throws<ConcordEmitException>(() => refused.Dispose());

            Assert.Contains(SoleHarmonyRefusalReason(log), failure.Message, StringComparison.Ordinal);
            Assert.True(handle.IsApplied);

            HarmonyOwnedTargets.Ran.Clear();
            Assert.Equal(2, HarmonyOwnedTargets.RefusedDispose(1));
            Assert.Contains("harmony", HarmonyOwnedTargets.Ran);
            Assert.Contains("concord", HarmonyOwnedTargets.Ran);
            Assert.Contains("body", HarmonyOwnedTargets.Ran);

            RefusingRebuildTranspiler.Refuse = false;
            handle.Dispose();

            Assert.False(handle.IsApplied);
            HarmonyOwnedTargets.Ran.Clear();
            Assert.Equal(2, HarmonyOwnedTargets.RefusedDispose(1));
            Assert.Contains("harmony", HarmonyOwnedTargets.Ran);
            Assert.Contains("body", HarmonyOwnedTargets.Ran);
            Assert.DoesNotContain("concord", HarmonyOwnedTargets.Ran);
        } finally {
            RefusingRebuildTranspiler.Refuse = false;
            handle?.Dispose();
            TestUnpatch.Own(foreign, id);
            MonoModEntryPoint.ResetForTests();
        }
    }

    [Fact]
    public void HarmonyRefusesTheRebuildForARecompose_RecomposeReturnsFalse_AndTheBodyInEffectIsUnchanged() {
        List<string> log = [];
        MethodInfo target = Method(nameof(HarmonyOwnedTargets.RefusedRecompose));
        const string id = "test.harmonyowned.refusedrecompose";
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony(id);
        IBackendHandle? handle = null;

        try {
            MonoModEntryPoint.Install(log.Add, null);
            handle = PatchBackend.Current.ApplyComposed(target, new[] { Note() });
            ArmRefusableHarmony(foreign, target);

            RefusingRebuildTranspiler.Refuse = true;
            Assert.False(PatchBackend.Current.Recompose(target));
            SoleHarmonyRefusalReason(log);

            HarmonyOwnedTargets.Ran.Clear();
            Assert.Equal(2, HarmonyOwnedTargets.RefusedRecompose(1));
            Assert.Contains("harmony", HarmonyOwnedTargets.Ran);
            Assert.Contains("concord", HarmonyOwnedTargets.Ran);
            Assert.Contains("body", HarmonyOwnedTargets.Ran);
        } finally {
            RefusingRebuildTranspiler.Refuse = false;
            handle?.Dispose();
            TestUnpatch.Own(foreign, id);
            MonoModEntryPoint.ResetForTests();
        }
    }

    [Fact]
    public void TheHostRefusesToRebuild_WhileItStillReportsThePatch_AndNothingIsReclaimed() {
        List<string> log = [];
        MethodInfo target = Method(nameof(HarmonyOwnedTargets.RebuildRefused));
        RoutingBackend router = new RoutingBackend(new MonoModBackend(), log.Add);
        AnsweringHost host = new AnsweringHost();
        router.ActivateHost(host);
        IBackendHandle? raw = null;
        IBackendHandle? routed = null;

        try {
            raw = router.ApplyComposed(target, new[] { Note() });
            Assert.True(raw.IsApplied);

            host.Owns = true;
            host.Rebuilds = true;
            routed = router.ApplyComposed(target, new[] { SecondNote() });

            Assert.Equal(RouteState.HarmonyOwned, router.GetRoute(target));
            Assert.True(routed.IsApplied);
            Assert.True(TargetDetourRegistry.IsSuperseded(target));

            host.Rebuilds = false;
            ConcordEmitException failure = Assert.Throws<ConcordEmitException>(
                () => router.ApplyComposed(target, new[] { ThirdNote() }));

            Assert.Contains(AnsweringHost.Refusal, failure.Message, StringComparison.Ordinal);
            Assert.True(host.HasPatch(target));
            Assert.Contains(AnsweringHost.Refusal, SoleRefusalReason(log), StringComparison.Ordinal);
            Assert.True(routed.IsApplied);
            Assert.True(raw.IsApplied);
            Assert.Equal(RouteState.HarmonyOwned, router.GetRoute(target));
            Assert.True(TargetDetourRegistry.IsSuperseded(target));
        } finally {
            host.Rebuilds = true;
            routed?.Dispose();
            raw?.Dispose();
            PatchOwners.Host = null;
        }
    }

    [Fact]
    public void TheHostRebuilds_TheMethodStaysHarmonyOwned() {
        List<string> log = [];
        MethodInfo target = Method(nameof(HarmonyOwnedTargets.RebuildAccepted));
        RoutingBackend router = new RoutingBackend(new MonoModBackend(), log.Add);
        AnsweringHost host = new AnsweringHost { Owns = true, Rebuilds = true };
        router.ActivateHost(host);
        IBackendHandle? routed = null;

        try {
            routed = router.ApplyComposed(target, new[] { Note() });

            Assert.Equal(1, host.RebuildRequests);
            Assert.Equal(RouteState.HarmonyOwned, router.GetRoute(target));
            Assert.True(routed.IsApplied);
            Assert.DoesNotContain(log, line => line.StartsWith(CoexistenceLogMarkers.RebuildRefused, StringComparison.Ordinal));
        } finally {
            routed?.Dispose();
            PatchOwners.Host = null;
        }
    }

    [Fact]
    public void HarmonyPatchesAMethodWithNoConcordInjection_NoHostOwnedLineForIt() {
        List<string> log = [];
        MethodInfo foreignOnly = Method(nameof(HarmonyOwnedTargets.ForeignOnly));
        MethodInfo shared = Method(nameof(HarmonyOwnedTargets.ForeignAndConcord));
        const string id = "test.harmonyowned.foreignonly";
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony(id);
        IBackendHandle? handle = null;

        try {
            MonoModEntryPoint.Install(log.Add, null);
            handle = PatchBackend.Current.ApplyComposed(shared, new[] { Note() });

            foreign.Patch(foreignOnly, prefix: new HarmonyMethod(Mod(nameof(HarmonyOwnedMods.HarmonyPrefix))));
            foreign.Patch(shared, prefix: new HarmonyMethod(Mod(nameof(HarmonyOwnedMods.HarmonyPrefix))));

            Assert.Contains(log, line => line.StartsWith(CoexistenceLogMarkers.HostOwned, StringComparison.Ordinal) && line.Contains(shared.Name));
            Assert.DoesNotContain(
                log,
                line => line.StartsWith(CoexistenceLogMarkers.HostOwned, StringComparison.Ordinal) && line.Contains(foreignOnly.Name));
        } finally {
            handle?.Dispose();
            TestUnpatch.Own(foreign, id);
            MonoModEntryPoint.ResetForTests();
        }
    }

    private static void ArmRefusableHarmony(HarmonyLib.Harmony foreign, MethodInfo target) {
        foreign.Patch(
            target,
            prefix: new HarmonyMethod(Mod(nameof(HarmonyOwnedMods.HarmonyPrefix))),
            transpiler: new HarmonyMethod(typeof(RefusingRebuildTranspiler).GetMethod(nameof(RefusingRebuildTranspiler.Maybe))!));
    }

    private static string SoleRefusalReason(List<string> log) {
        List<string> refusals = log.FindAll(line => line.StartsWith(CoexistenceLogMarkers.RebuildRefused, StringComparison.Ordinal));
        Assert.Single(refusals);
        return refusals[0].Substring(refusals[0].IndexOf(": ", StringComparison.Ordinal) + 2);
    }

    private static string SoleHarmonyRefusalReason(List<string> log) {
        string reason = SoleRefusalReason(log);
        Assert.Contains("threw while rebuilding", reason, StringComparison.Ordinal);
        return reason;
    }

    private static MethodInfo Method(string name) {
        return typeof(HarmonyOwnedTargets).GetMethod(name)!;
    }

    private static MethodInfo Transpiler(string name) {
        return typeof(HarmonyBuildTranspilers).GetMethod(name)!;
    }

    private static MethodInfo Mod(string name) {
        return typeof(HarmonyOwnedMods).GetMethod(name)!;
    }

    private static Injection Note() {
        return new Injection(Mod(nameof(HarmonyOwnedMods.ConcordNote)), new InjectAt.Head(), "test.harmonyowned.concord", 0);
    }

    private static Injection SecondNote() {
        return new Injection(Mod(nameof(HarmonyOwnedMods.ConcordSecondNote)), new InjectAt.Head(), "test.harmonyowned.concord.two", 0);
    }

    private static Injection ThirdNote() {
        return new Injection(Mod(nameof(HarmonyOwnedMods.ConcordThirdNote)), new InjectAt.Head(), "test.harmonyowned.concord.three", 0);
    }

    private sealed class AnsweringHost : IForeignPatchHost {
        internal const string Refusal = "this host answers with the reason it kept the old wrapper";

        internal bool Owns { get; set; }

        internal bool Rebuilds { get; set; }

        internal int RebuildRequests { get; private set; }

        public IDisposable? EnterHostLock() {
            return null;
        }

        public bool HasPatch(MethodBase target) {
            return Owns;
        }

        public bool TryRebuild(MethodBase target, out string refusals) {
            RebuildRequests++;
            refusals = Rebuilds ? string.Empty : Refusal;
            return Rebuilds;
        }

        public IReadOnlyList<string> ForeignOwners(MethodBase target) {
            return [];
        }

        public IReadOnlyList<string> ConcordOwners(MethodBase target) {
            return [];
        }

        public bool TryInstallNotifier(IForeignPatchObserver observer, MonoModBackend rawBackend) {
            return true;
        }
    }
}
