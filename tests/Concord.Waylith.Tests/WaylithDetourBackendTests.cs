using System.Reflection;
using System.Reflection.Emit;
using Concord.Backend;
using Concord.Emit;
using Xunit;
using OpCode = System.Reflection.Emit.OpCode;

namespace Concord.Waylith.Tests;

public static class PatchSubject {
    public static int Touched { get; set; }

    public static void Marker() {
        Touched++;
    }

    public static void SecondMarker() {
        Touched += 2;
    }

    public static int Spanned(int left, int right) {
        int first = left + right;
        int second = first * left;
        int third = second - right;
        return first + second + third;
    }
}

public sealed class JudgeFixtureSubject {
    public int InstanceMarker() {
        return 1;
    }
}

public sealed class JudgeFixtureBox<T> {
    public int Get() {
        return 1;
    }
}

[Collection("AdapterStatics")]
public class WaylithDetourBackendTests : IDisposable {
    private static readonly MethodInfo Spanned = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Spanned))!;

    private readonly string? previousEmptyMapVariable =
        Environment.GetEnvironmentVariable("CONCORD_WAYLITH_EMPTY_MAP");

    public void Dispose() {
        WaylithClrBackend.EmptyMapAlways = false;
        Environment.SetEnvironmentVariable("CONCORD_WAYLITH_EMPTY_MAP", previousEmptyMapVariable);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TwoInjectedInstructionsInOneGapGetTwoDifferentReportedOffsets() {
        int original = Len(Spanned);
        ComposedBody body = new ComposedBody(
            [Copied(OpCodes.Nop, 0), Injected(OpCodes.Nop), Injected(OpCodes.Nop), Copied(OpCodes.Ret, 1)],
            [],
            [],
            false);
        EncodedBody encoded = new EncodedBody([], [0, 1, 2, 3], 1, 4);

        IReadOnlyList<OffsetPair> map = WaylithClrBackend.OffsetMap(Spanned, body, encoded);

        int first = Assert.Single(map, pair => pair.Replaced == 1).Original;
        int second = Assert.Single(map, pair => pair.Replaced == 2).Original;
        Assert.NotEqual(first, second);
        Assert.True(first >= original, $"the first injected instruction reported {first}, inside the {original}-byte original body");
        Assert.True(second >= original, $"the second injected instruction reported {second}, inside the {original}-byte original body");
    }

    [Fact]
    public void OffsetMapKeepsTargetOffsetsAndSentinelsEverythingElse() {
        int band = Len(Spanned);
        Assert.True(band > 9);
        ComposedBody body = new ComposedBody(
            [
                Copied(OpCodes.Nop, 5),
                Injected(OpCodes.Nop),
                Copied(OpCodes.Nop, 9),
                Rewritten(OpCodes.Ret),
            ],
            [],
            [],
            false);

        EncodedBody encoded = new EncodedBody([], [0, 1, 2, 3], 1, 4);
        IReadOnlyList<OffsetPair> map = WaylithClrBackend.OffsetMap(Spanned, body, encoded);

        Assert.Equal(
            [(5, 0), (band + 1, 1), (9, 2), (band + 3, 3)],
            map.Select(pair => (pair.Original, pair.Replaced)));
    }

    [Fact]
    public void NoSentinelEqualsARealTargetOffsetWhenTheTwoRangesAreAdjacent() {
        int band = Len(Spanned);
        ComposedInstruction[] stream = new ComposedInstruction[band + 1];
        int[] offsets = new int[band + 1];
        stream[0] = Injected(OpCodes.Nop);
        offsets[0] = 0;
        for (int i = 0; i < band; i++) {
            stream[i + 1] = Copied(OpCodes.Nop, i);
            offsets[i + 1] = i + 1;
        }

        EncodedBody encoded = new EncodedBody([], offsets, 1, band + 1);

        IReadOnlyList<OffsetPair> map = WaylithClrBackend.OffsetMap(
            Spanned,
            new ComposedBody(stream, [], [], false),
            encoded);

        Assert.Equal(band, map[0].Original);
        Assert.Equal(Enumerable.Range(0, band), map.Skip(1).Select(pair => pair.Original));
        Assert.Equal(map.Count, map.Select(pair => pair.Original).Distinct().Count());
    }

    [Fact]
    public void OffsetMapEmitsInBodyOrderAndCollapsesAdjacentRuns() {
        ComposedBody body = new ComposedBody(
            [Copied(OpCodes.Nop, 7), Copied(OpCodes.Nop, 2), Copied(OpCodes.Nop, 2), Copied(OpCodes.Ret, 7)],
            [],
            [],
            false);

        EncodedBody encoded = new EncodedBody([], [40, 10, 60, 90], 1, 4);
        IReadOnlyList<OffsetPair> map = WaylithClrBackend.OffsetMap(Spanned, body, encoded);

        Assert.Equal([(7, 40), (2, 10), (7, 90)], map.Select(pair => (pair.Original, pair.Replaced)));
    }

    [Fact]
    public void OffsetMapRefusesAnEncodedBodyWhoseOffsetCountDoesNotMatch() {
        ComposedBody body = new ComposedBody([Copied(OpCodes.Ret, 0)], [], [], false);
        EncodedBody encoded = new EncodedBody([], [0, 4], 1, 2);

        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => WaylithClrBackend.OffsetMap(Spanned, body, encoded));

        Assert.Contains("cannot be paired into an offset map", failure.Message);
    }

    [Fact]
    public void TheSwitchDropsEveryPair() {
        WaylithClrBackend.EmptyMapAlways = true;
        ComposedBody body = new ComposedBody(
            [Copied(OpCodes.Nop, 5), Injected(OpCodes.Nop), Copied(OpCodes.Ret, 9)],
            [],
            [],
            false);
        EncodedBody encoded = new EncodedBody([], [0, 1, 2], 1, 3);

        Assert.Empty(WaylithClrBackend.OffsetMap(Spanned, body, encoded));
    }

    [Fact]
    public void WithNothingInTheEnvironmentTwoOwnersKeepTheSentinelMap() {
        Assert.NotEmpty(TwoOwnerMap());
    }

    [Fact]
    public void TheEnvironmentAloneCanEmptyTheMapAtTwoOwnersFromOutsideTheProcess() {
        Environment.SetEnvironmentVariable("CONCORD_WAYLITH_EMPTY_MAP", "on");

        Assert.Empty(TwoOwnerMap());
    }

    [Fact]
    public void TheEnvironmentTurnedOffLeavesTheSentinelMapInPlace() {
        Environment.SetEnvironmentVariable("CONCORD_WAYLITH_EMPTY_MAP", "off");

        Assert.NotEmpty(TwoOwnerMap());
    }

    [Fact]
    public void AnUnreadableEnvironmentValueIsReportedAndThenLeavesTheSentinelMapInPlace() {
        AdapterEntryPoint.ResetForTests();
        Action<string> previous = WaylithClrBackend.Log;
        List<string> logged = [];
        WaylithClrBackend.Log = logged.Add;

        try {
            Environment.SetEnvironmentVariable("CONCORD_WAYLITH_EMPTY_MAP", "maybe");

            Assert.NotEmpty(TwoOwnerMap());
            Assert.Contains(logged, line => line.Contains("is set to 'maybe'", StringComparison.Ordinal));
        } finally {
            WaylithClrBackend.Log = previous;
            AdapterEntryPoint.ResetForTests();
        }
    }

    [Fact]
    public void AnInProcessTrueSurvivesAnEnvironmentThatSaysOff() {
        Environment.SetEnvironmentVariable("CONCORD_WAYLITH_EMPTY_MAP", "off");
        WaylithClrBackend.EmptyMapAlways = true;

        Assert.Empty(TwoOwnerMap());
    }

    [Fact]
    public void TwoOwnersStillGetSentinelsSoInjectedCodeIsToldApartFromTheTargetsOwnIl() {
        int band = Len(Spanned);
        ComposedBody body = new ComposedBody(
            [
                Copied(OpCodes.Nop, 5),
                InjectedBy(OpCodes.Nop, nameof(PatchSubject.Marker)),
                InjectedBy(OpCodes.Nop, nameof(PatchSubject.SecondMarker)),
                Copied(OpCodes.Ret, 9),
            ],
            [],
            [],
            false);
        EncodedBody encoded = new EncodedBody([], [0, 1, 2, 3], 1, 4);

        IReadOnlyList<OffsetPair> map =
            WaylithClrBackend.OffsetMap(Spanned, body, encoded);

        Assert.Equal(
            [(5, 0), (band + 1, 1), (band + 2, 2), (9, 3)],
            map.Select(pair => (pair.Original, pair.Replaced)));
        Assert.NotEqual(map[1].Original, map[2].Original);
        Assert.All(map.Skip(1).Take(2), pair => Assert.True(pair.Original >= band));
    }

    [Fact]
    public void ABodyWithNoOriginalIlIsAllSentinelAndStillDistinguishesItsInstructions() {
        int band = Len(Spanned);
        ComposedBody body = new ComposedBody([Injected(OpCodes.Nop), Injected(OpCodes.Ret)], [], [], false);
        EncodedBody encoded = new EncodedBody([], [0, 1], 1, 2);

        IReadOnlyList<OffsetPair> map = WaylithClrBackend.OffsetMap(Spanned, body, encoded);

        Assert.Equal([(band, 0), (band + 1, 1)], map.Select(pair => (pair.Original, pair.Replaced)));
    }

    [Fact]
    public void ABodyTooLargeForTheMeasuredSentinelRangeGetsNoMapAndSaysWhy() {
        ComposedBody body = new ComposedBody([Copied(OpCodes.Nop, 0), Injected(OpCodes.Ret)], [], [], false);
        EncodedBody encoded = new EncodedBody([], [0, 1], 1, WaylithClrBackend.SentinelCeiling);

        Assert.Equal(-1, WaylithClrBackend.SentinelBand(Spanned, encoded));
        Assert.Empty(WaylithClrBackend.OffsetMap(Spanned, body, encoded));
        Assert.Contains(
            "runs past the offset the runtime is known to carry",
            WaylithClrBackend.NoOffsetMapWarning(Spanned, 1),
            StringComparison.Ordinal);
        Assert.Contains(
            "runs past the offset the runtime is known to carry",
            WaylithClrBackend.NoOffsetMapWarning(Spanned, 2),
            StringComparison.Ordinal);

        WaylithClrBackend.EmptyMapAlways = true;

        Assert.Contains(
            AdapterEntryPoint.EmptyMapVariable + " is on, so no map is written for any body",
            WaylithClrBackend.NoOffsetMapWarning(Spanned, 2),
            StringComparison.Ordinal);
        Assert.Contains(
            AdapterEntryPoint.EmptyMapVariable + " is on, so no map is written for any body",
            WaylithClrBackend.NoOffsetMapWarning(Spanned, 1),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheWholeSentinelBandStaysInsideTheRangeTheProbeMeasured() {
        EncodedBody largest = new EncodedBody([], [0], 1, WaylithClrBackend.SentinelCeiling - Len(Spanned));
        int band = WaylithClrBackend.SentinelBand(Spanned, largest);

        Assert.Equal(Len(Spanned), band);
        Assert.InRange((long)band + largest.CodeSize - 1, 0, WaylithClrBackend.SentinelCeiling);
        Assert.Equal(-1, WaylithClrBackend.SentinelBand(Spanned, new EncodedBody([], [0], 1, largest.CodeSize + 1)));
    }

    private static IReadOnlyList<OffsetPair> TwoOwnerMap() {
        ComposedBody body = new ComposedBody(
            [Copied(OpCodes.Nop, 5), Injected(OpCodes.Nop), Copied(OpCodes.Ret, 9)],
            [],
            [],
            false);
        EncodedBody encoded = new EncodedBody([], [0, 1, 2], 1, 3);

        return WaylithClrBackend.OffsetMap(Spanned, body, encoded);
    }

    private static ComposedInstruction Copied(OpCode opcode, int origin) {
        return new ComposedInstruction(new CodeInstruction(opcode), origin, null);
    }

    private static ComposedInstruction Injected(OpCode opcode) {
        return new ComposedInstruction(new CodeInstruction(opcode), null, Spanned);
    }

    private static ComposedInstruction InjectedBy(OpCode opcode, string injection) {
        return new ComposedInstruction(
            new CodeInstruction(opcode), null, typeof(PatchSubject).GetMethod(injection)!);
    }

    private static ComposedInstruction Rewritten(OpCode opcode) {
        return new ComposedInstruction(new CodeInstruction(opcode), null, null);
    }

    private static int Len(MethodBase target) {
        return target.GetMethodBody()!.GetILAsByteArray()!.Length;
    }

    [Fact]
    public void Judge_accepts_a_plain_method() {
        IPatchBackend backend = new WaylithClrBackend(new NoOpBodyOwner());
        MethodBase target = typeof(JudgeFixtureSubject).GetMethod(nameof(JudgeFixtureSubject.InstanceMarker))!;

        TargetVerdict verdict = backend.Judge(target);

        Assert.Equal(TargetHandling.Accept, verdict.Handling);
    }

    [Fact]
    public void Judge_refuses_a_closed_shared_body_generic() {
        IPatchBackend backend = new WaylithClrBackend(new NoOpBodyOwner());
        MethodBase target = typeof(JudgeFixtureBox<int>).GetMethod(nameof(JudgeFixtureBox<int>.Get))!;

        TargetVerdict verdict = backend.Judge(target);

        Assert.Equal(TargetHandling.Refuse, verdict.Handling);
        Assert.Equal(StagePublisher.RefusedCode, verdict.Code);
    }

    private sealed class NoOpBodyOwner : ITargetBodyOwner {
        public int PublishedTargetCount => 0;

        public bool PublishComposed(MethodBase target, IReadOnlyList<Injection> ordered) {
            return true;
        }

        public void RevertToOriginal(MethodBase target) {
        }

        public bool IsPublished(MethodBase target) {
            return false;
        }

        public bool Rebuild(MethodBase target) {
            return false;
        }
    }
}
