using System.Reflection;
using Xunit;

namespace Concord.Host.Adapter.Tests;

[Collection("AdapterStatics")]
public sealed class EmptyMapSwitchTests : IDisposable {
    private readonly string? before = Environment.GetEnvironmentVariable("CONCORD_WAYLITH_EMPTY_MAP");

    public EmptyMapSwitchTests() {
        Environment.SetEnvironmentVariable("CONCORD_WAYLITH_EMPTY_MAP", null);
        AdapterEntryPoint.ResetForTests();
    }

    public void Dispose() {
        Environment.SetEnvironmentVariable("CONCORD_WAYLITH_EMPTY_MAP", before);
        AdapterEntryPoint.ResetForTests();
    }

    [Fact]
    public void TheVariableTheFallbackReadsIsTheOneDocumentedOutsideTheProcess() {
        Assert.Equal("CONCORD_WAYLITH_EMPTY_MAP", AdapterEntryPoint.EmptyMapVariable);
    }

    [Fact]
    public void ATransformNestedOnOneThreadPutsTheOuterStagedBodyBack() {
        RuntimeMethodHandle outerMethod = typeof(EmptyMapSwitchTests).GetMethod(nameof(Sink), BindingFlags.NonPublic | BindingFlags.Static)!.MethodHandle;
        RuntimeMethodHandle innerMethod = typeof(EmptyMapSwitchTests).GetMethod(nameof(Set), BindingFlags.NonPublic | BindingFlags.Static)!.MethodHandle;
        ComposedBody outerBody = Empty();
        ComposedBody innerBody = Empty();

        Assert.Null(AdapterEntryPoint.StageInput(outerMethod, outerBody));

        StageInputSlot? displaced = AdapterEntryPoint.StageInput(innerMethod, innerBody);
        Assert.NotNull(displaced);
        Assert.Equal(outerMethod, displaced!.Method);
        Assert.Same(outerBody, displaced.Input);

        AdapterEntryPoint.RestoreStageInput(displaced);

        StageInputSlot? afterInner = AdapterEntryPoint.StageInput(innerMethod, innerBody);
        Assert.NotNull(afterInner);
        Assert.Equal(outerMethod, afterInner!.Method);
        Assert.Same(outerBody, afterInner.Input);

        AdapterEntryPoint.RestoreStageInput(null);
        Assert.Null(AdapterEntryPoint.StageInput(outerMethod, outerBody));
        AdapterEntryPoint.RestoreStageInput(null);
    }

    [Fact]
    public void NothingSetLeavesTheFallbackOff() {
        Assert.False(AdapterEntryPoint.EmptyMapRequested(Sink()));
        Assert.False(WaylithDetourBackend.EmptyMapAlways);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("on")]
    [InlineData("YES")]
    [InlineData("  enabled  ")]
    public void EveryOnSpellingTakesTheWayBack(string raw) {
        Set(raw);

        Assert.True(AdapterEntryPoint.EmptyMapRequested(Sink()));
        Assert.True(WaylithDetourBackend.EmptyMapAlways);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("Off")]
    [InlineData("no")]
    [InlineData("disabled")]
    [InlineData("")]
    [InlineData("   ")]
    public void EveryOffSpellingAndAnEmptyValueLeaveItOff(string raw) {
        Set(raw);

        Assert.False(AdapterEntryPoint.EmptyMapRequested(Sink()));
        Assert.False(WaylithDetourBackend.EmptyMapAlways);
    }

    [Theory]
    [InlineData("2")]
    [InlineData("offf")]
    [InlineData("empty")]
    public void AValueNobodyCanReadIsReportedAndThenTreatedAsOff(string raw) {
        Set(raw);
        List<string> written = [];

        Assert.False(AdapterEntryPoint.EmptyMapRequested(written.Add));

        string line = Assert.Single(written);
        Assert.Contains(AdapterEntryPoint.EmptyMapVariable + " is set to '" + raw + "'", line, StringComparison.Ordinal);
        Assert.Contains("which is neither", line, StringComparison.Ordinal);
        Assert.EndsWith(
            ". Ignoring it, so every body keeps the sentinel offset map that tells injected code apart from the target's own il.",
            line,
            StringComparison.Ordinal);
    }

    [Fact]
    public void OneUnreadableValueIsReportedOnceNotOncePerMethodPublished() {
        Set("2");
        List<string> written = [];

        for (int i = 0; i < 5; i++) {
            Assert.False(AdapterEntryPoint.EmptyMapRequested(written.Add));
        }

        Assert.Single(written);
    }

    [Fact]
    public void ASecondUnreadableValueIsReportedOnItsOwn() {
        List<string> written = [];

        Set("2");
        _ = AdapterEntryPoint.EmptyMapRequested(written.Add);
        Set("3");
        _ = AdapterEntryPoint.EmptyMapRequested(written.Add);

        Assert.Equal(2, written.Count);
    }

    [Fact]
    public void TheReportIsForgottenWhenTheAdapterIsResetSoNoTestInheritsIt() {
        Set("2");
        List<string> written = [];

        _ = AdapterEntryPoint.EmptyMapRequested(written.Add);
        AdapterEntryPoint.ResetForTests();
        _ = AdapterEntryPoint.EmptyMapRequested(written.Add);

        Assert.Equal(2, written.Count);
    }

    [Fact]
    public void ReadingTheVariableNeverThrowsWhateverIsInIt() {
        foreach (string raw in new[] { "\t", "on;off", "=", new string('x', 4096) }) {
            Set(raw);

            Assert.False(AdapterEntryPoint.EmptyMapRequested(Sink()));
        }
    }

    [Fact]
    public void AnInProcessTrueIsNotLoweredByTheEnvironment() {
        Set("off");
        WaylithDetourBackend.EmptyMapAlways = true;

        try {
            Assert.True(WaylithDetourBackend.EmptyMapAlways);
        } finally {
            WaylithDetourBackend.EmptyMapAlways = false;
        }
    }

    [Fact]
    public void AnInProcessFalseDoesNotPinTheFallbackAgainstTheEnvironment() {
        WaylithDetourBackend.EmptyMapAlways = false;
        Set("on");

        Assert.True(WaylithDetourBackend.EmptyMapAlways);
    }

    [Fact]
    public void TheEnvironmentIsReadEveryTimeNotOnceAtStartup() {
        Set("on");
        Assert.True(WaylithDetourBackend.EmptyMapAlways);
        Set("off");
        Assert.False(WaylithDetourBackend.EmptyMapAlways);
        Set("on");
        Assert.True(WaylithDetourBackend.EmptyMapAlways);
    }

    private static Action<string> Sink() {
        return _ => {
        };
    }

    private static ComposedBody Empty() {
        return new ComposedBody([], [], [], false);
    }

    private static void Set(string? value) {
        Environment.SetEnvironmentVariable("CONCORD_WAYLITH_EMPTY_MAP", value);
    }
}
