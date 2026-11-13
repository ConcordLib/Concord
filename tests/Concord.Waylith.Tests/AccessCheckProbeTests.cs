using Xunit;

namespace Concord.Waylith.Tests;

public class AccessCheckProbeTests {
    [Fact]
    public void RimWorld17ShipsTheKeyAsABooleanTrue() {
        Assert.True(RuntimeCapability.SkipsAccessChecks(true, null));
    }

    [Fact]
    public void AHostThatSetsTheKeyFalseKeepsTheChecks() {
        Assert.False(RuntimeCapability.SkipsAccessChecks(false, null));
    }

    [Fact]
    public void AStockRuntimeCarriesNeitherSource() {
        Assert.False(RuntimeCapability.SkipsAccessChecks(null, null));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    public void TheKeyIsHonoredWhenTheHostWroteItAsText(string configured) {
        Assert.True(RuntimeCapability.SkipsAccessChecks(configured, null));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("TRUE")]
    public void TheEnvironmentSwitchTurnsTheChecksOff(string environment) {
        Assert.True(RuntimeCapability.SkipsAccessChecks(null, environment));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("")]
    [InlineData("no")]
    public void AnUnsetOrDeniedEnvironmentSwitchKeepsTheChecks(string environment) {
        Assert.False(RuntimeCapability.SkipsAccessChecks(null, environment));
    }

    [Fact]
    public void TheRuntimeConfigKeyWinsOverTheEnvironmentSwitch() {
        Assert.False(RuntimeCapability.SkipsAccessChecks(false, "1"));
    }

    [Fact]
    public void ASwitchThatSaysSkipAnswersFirstAndTheProbeNeverRuns() {
        bool probed = false;

        (bool ignores, string source) = RuntimeCapability.JudgeAccessChecks(true, () => {
            probed = true;
            return new MethodAccessException("checked");
        });

        Assert.True(ignores);
        Assert.False(probed);
        Assert.Contains(RuntimeCapability.AccessCheckKey, source);
    }

    [Fact]
    public void AHostThatLeavesTheSwitchOutAndHasNoProbeKeepsTheChecks() {
        (bool ignores, string source) = RuntimeCapability.JudgeAccessChecks(false, null);

        Assert.False(ignores);
        Assert.Contains("no probe", source);
    }

    [Fact]
    public void AHostThatLeavesTheSwitchOutDropsTheChecksWhenTheProbeRunsClean() {
        (bool ignores, string source) = RuntimeCapability.JudgeAccessChecks(false, static () => null);

        Assert.True(ignores);
        Assert.Contains("ran it clean", source);
    }

    [Fact]
    public void AProbeThatReportsAFailureKeepsTheChecksAndTheSourceNamesIt() {
        (bool ignores, string source) = RuntimeCapability.JudgeAccessChecks(
            false,
            static () => new MethodAccessException("checked"));

        Assert.False(ignores);
        Assert.Contains("threw MethodAccessException: checked", source);
    }

    [Theory]
    [InlineData(typeof(FieldAccessException))]
    [InlineData(typeof(TypeAccessException))]
    [InlineData(typeof(InvalidProgramException))]
    [InlineData(typeof(MissingMemberException))]
    public void AProbeThatThrowsAnyExceptionKeepsTheChecks(Type thrown) {
        (bool ignores, string source) = RuntimeCapability.JudgeAccessChecks(
            false,
            () => throw (Exception)Activator.CreateInstance(thrown)!);

        Assert.False(ignores);
        Assert.Contains("threw " + thrown.Name, source);
    }

    [Fact]
    public void AStockRuntimeHasNoPipelineSoTheChecksStayOn() {
        Assert.False(RuntimeCapability.HasPipeline);
        Assert.False(RuntimeCapability.IgnoresAccessChecks);
    }
}
