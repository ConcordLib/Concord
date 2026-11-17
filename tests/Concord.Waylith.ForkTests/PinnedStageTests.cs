using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Concord.Waylith.ForkTests;

public static class PinnedStageVictims {
    public static int Heads;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int PatchedAfterThePin() {
        return 41;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int PatchedBeforeThePin() {
        return 51;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int PatchedUnderAThrowingHook() {
        return 61;
    }

    public static void CountHead() {
        Heads++;
    }
}

[Collection(ForkFieldInjection.ApplyCollection)]
public sealed class PinnedStageTests : IDisposable {
    private const string NoHook =
        "No pipeline or no host regenerate hook on this runtime, so nothing can put a pinned stage back on a method.";

    private const string HookRefusal = "the hook refused this method";

    private const int PinnedValue = 99;

    private readonly Delegate? hookOnEntry = ForkInjection.RegenerateHook;

    private readonly Action<string> logOnEntry = WaylithClrBackend.Log;

    private readonly ConcurrentQueue<string> lines = new ConcurrentQueue<string>();

    private readonly List<MethodInfo> patched = [];

    private StandInTransformer? standIn;

    private RuntimeMethodHandle victim;

    private byte[]? pinned;

    public static bool Unavailable => ForkRuntime.Unavailable || !ForkRuntime.HostRead || ForkInjection.HookMissing;

    public void Dispose() {
        ForkInjection.RegenerateHook = hookOnEntry;
        WaylithClrBackend.Log = logOnEntry;
        foreach (MethodInfo target in patched) {
            ForkRuntime.Restore(target);
        }

        standIn?.Unregister();
        PinnedStageVictims.Heads = 0;
    }

    [Fact(Skip = NoHook, SkipWhen = nameof(Unavailable))]
    public void AStageThatPinsItsInputStaysOnAMethodConcordPatchesAfterIt() {
        MethodInfo target = Victim(nameof(PinnedStageVictims.PatchedAfterThePin));
        Pin(target);
        AssertStillPinned(PinnedStageVictims.PatchedAfterThePin(), "before any Concord patch");

        IPatchHandle patch = Patch(target);

        Assert.True(
            patch.IsApplied,
            "the patch reports IsApplied false, so Concord staged nothing over the victim and the pinned stage's "
            + "input never changed.");
        AssertInputChanged();
        AssertStillPinned(PinnedStageVictims.PatchedAfterThePin(), "after Concord patched the method");
    }

    [Fact(Skip = NoHook, SkipWhen = nameof(Unavailable))]
    public void AStageThatPinsItsInputStaysOnAMethodConcordUnpatches() {
        MethodInfo target = Victim(nameof(PinnedStageVictims.PatchedBeforeThePin));
        IPatchHandle patch = Patch(target);

        Assert.True(
            patch.IsApplied,
            "the patch reports IsApplied false, so there is no Concord body for the pinned stage to build on.");
        Pin(target);
        AssertStillPinned(PinnedStageVictims.PatchedBeforeThePin(), "while the Concord patch is live");

        patch.Dispose();

        AssertInputChanged();
        AssertStillPinned(PinnedStageVictims.PatchedBeforeThePin(), "after Concord unpatched the method");
    }

    [Fact(Skip = NoHook, SkipWhen = nameof(Unavailable))]
    public void AHookThatThrowsLeavesTheConcordPatchRunningAndIsReportedOnce() {
        MethodInfo target = Victim(nameof(PinnedStageVictims.PatchedUnderAThrowingHook));
        victim = StandInTransformer.HandleOf(target);
        WaylithClrBackend.Log = lines.Enqueue;
        ForkInjection.UseRegenerateHook(Refuse);

        IPatchHandle patch = Patch(target);

        Assert.True(
            patch.IsApplied,
            "the patch reports IsApplied false, so a hook that threw took the Concord patch down with it.");
        Assert.Equal(61, PinnedStageVictims.PatchedUnderAThrowingHook());
        Assert.Equal(1, PinnedStageVictims.Heads);

        string[] reports = [.. lines.Where(static line => line.StartsWith(HostRegenerate.FailedMarker, StringComparison.Ordinal))];
        Assert.True(
            reports.Length == 1 && reports[0].Contains(HookRefusal, StringComparison.Ordinal),
            $"wanted one '{HostRegenerate.FailedMarker}' line carrying the hook's own message, and the log has "
            + $"{reports.Length}: {string.Join(" | ", lines)}");
    }

    private static MethodInfo Victim(string name) {
        return typeof(PinnedStageVictims).GetMethod(name)!;
    }

    private static void AssertStillPinned(int returned, string when) {
        Assert.True(
            returned == PinnedValue,
            $"the victim returned {returned} {when}, not {PinnedValue}, so the pinned stage's body is not the one "
            + "running.");
    }

    private IPatchHandle Patch(MethodInfo target) {
        patched.Add(target);
        return ForkRuntime.Apply(target, typeof(PinnedStageVictims), nameof(PinnedStageVictims.CountHead), At.Head);
    }

    private void Pin(MethodInfo target) {
        victim = StandInTransformer.HandleOf(target);
        standIn = new StandInTransformer(0, Rewrite);
        standIn.Register();
        ForkInjection.UseRegenerateHook(Rebase);

        Exception? failure = standIn.Rebuild(target);

        Assert.Null(failure);
    }

    private byte[] Rewrite(byte[] input) {
        pinned ??= input;
        if (!pinned.AsSpan().SequenceEqual(input)) {
            throw new InvalidOperationException(StandInTransformer.Refusal);
        }

        return ForkRuntime.Returning(PinnedValue);
    }

    private bool Rebase(MethodBase stale) {
        if (standIn is null || !StandInTransformer.HandleOf(stale).Equals(victim)) {
            return false;
        }

        pinned = null;
        return standIn.Rebuild(stale) is null;
    }

    private bool Refuse(MethodBase stale) {
        if (!StandInTransformer.HandleOf(stale).Equals(victim)) {
            return false;
        }

        throw new InvalidOperationException(HookRefusal);
    }

    private void AssertInputChanged() {
        IReadOnlyList<byte[]> seen = standIn!.SeenIl;
        Assert.True(
            seen.Count > 1 && !seen[0].AsSpan().SequenceEqual(seen[^1]),
            $"the pinned stage ran {seen.Count} time(s) and never saw a body other than its first, so Concord's "
            + "stage did not change its input and the value below proves nothing about a rebuild.");
    }
}
