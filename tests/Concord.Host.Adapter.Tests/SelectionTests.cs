using System.Reflection;
using Concord.Detour;
using Concord.Emit;
using Xunit;

namespace Concord.Host.Adapter.Tests;

public sealed class CapabilityTests {
    [Fact]
    public void AnAbsentApiIsTheOnlyAnswerWhenTheRuntimeDoesNotShipTheType() {
        Assert.Equal(BodyReplacement.ApiAbsent, Support(null, true, true));
        Assert.Equal(BodyReplacement.ApiAbsent, Support(null, false, false));
    }

    [Fact]
    public void TheRuntimesOwnSwitchBeatsEverythingElseTheProbeRead() {
        Assert.Equal(BodyReplacement.DisabledByRuntime, Support(false, true, true));
        Assert.Equal(BodyReplacement.DisabledByRuntime, Support(false, false, false));
    }

    [Fact]
    public void AWholeApiStillNeedsTheTokenMintersToBind() {
        Assert.Equal(BodyReplacement.MintingAbsent, Support(true, false, true));
        Assert.Equal(BodyReplacement.Available, Support(true, true, false));
    }

    [Fact]
    public void ATransformerPipelineOnTheRuntimeIsItsOwnVerdictAndStillCountsAsReplacing() {
        Assert.Equal(BodyReplacement.PipelineAvailable, Support(true, true, true));
        Assert.True(RuntimeCapability.Replaces(BodyReplacement.PipelineAvailable));
        Assert.True(RuntimeCapability.Replaces(BodyReplacement.Available));

        foreach (BodyReplacement support in Enum.GetValues<BodyReplacement>()) {
            if (support is BodyReplacement.Available or BodyReplacement.PipelineAvailable) {
                continue;
            }

            Assert.False(
                RuntimeCapability.Replaces(support),
                $"{support} reads as a runtime that can replace a body, so the install would swap the backend in " +
                "on a runtime the probe just refused.");
        }
    }

    [Fact]
    public void AMissingMemberIsReportedAsAnApiThisWasNotBuiltAgainstRatherThanAFault() {
        (BodyReplacement support, string? failure) =
            RuntimeCapability.Refused(new MissingMethodException("MethodBodyUpdater.GetTokenForSignature"));

        Assert.Equal(BodyReplacement.ApiUnrecognized, support);
        Assert.Contains(nameof(MissingMethodException), failure!);
        Assert.Contains("GetTokenForSignature", failure!);
        Assert.Contains(failure!, RuntimeCapability.Explain(support, failure));
        Assert.Contains("no detail was captured", RuntimeCapability.Explain(support));
    }

    [Fact]
    public void AnyOtherThrowIsAMismatchBetweenTheManagedApiAndTheNativeRuntimeUnderIt() {
        (BodyReplacement support, string? failure) =
            RuntimeCapability.Refused(new EntryPointNotFoundException("MethodBodyUpdater_IsSupported"));

        Assert.Equal(BodyReplacement.ProbeFailed, support);
        Assert.Contains(nameof(EntryPointNotFoundException), failure!);
        Assert.Contains("MethodBodyUpdater_IsSupported", failure!);
        Assert.Contains(failure!, RuntimeCapability.Explain(support, failure));
        Assert.Contains("no detail was captured", RuntimeCapability.Explain(support));
    }

    [Fact]
    public void AThrowWrappedByReflectionIsJudgedOnWhatItWrapped() {
        (BodyReplacement support, string? failure) = RuntimeCapability.Refused(
            new TargetInvocationException(new MissingFieldException("MethodBodyUpdater.IsSupported")));

        Assert.Equal(BodyReplacement.ApiUnrecognized, support);
        Assert.Contains(nameof(MissingFieldException), failure!);
        Assert.DoesNotContain(nameof(TargetInvocationException), failure!);
        Assert.Throws<ArgumentNullException>(() => RuntimeCapability.Refused(null!));
    }

    [Theory]
    [InlineData(0x2B000001, true)]
    [InlineData(0x2B000000, true)]
    [InlineData(0x06000001, false)]
    [InlineData(0x0A000001, false)]
    [InlineData(0x11000013, false)]
    public void ADefinitionTokenInTheMethodSpecTableMeansTheRuntimeInstantiatesIt(int token, bool instantiates) {
        Assert.Equal(instantiates, RuntimeCapability.InstantiatesDefinitions(token));
    }

    [Fact]
    public void AMethodSpecVerdictReadsTheTopByteUnsignedSoAHighTableIsNotMistakenForOne() {
        Assert.False(RuntimeCapability.InstantiatesDefinitions(unchecked((int)0xFF000001)));
        Assert.False(RuntimeCapability.InstantiatesDefinitions(-1));
    }

    [Fact]
    public void AMintThatHandsBackAMethodSpecIsTheOnlyRuntimeThatCanNameAGenericMethod() {
        Assert.True(RuntimeCapability.MintsMethodSpecsThrough(() => 0x2B000001));
        Assert.False(RuntimeCapability.MintsMethodSpecsThrough(() => 0x06000001));
        Assert.False(RuntimeCapability.MintsMethodSpecsThrough(() => 0x0A000001));
    }

    [Fact]
    public void AMintThatThrowsCountsAsARefusalRatherThanTakingTheProcessDown() {
        Assert.False(RuntimeCapability.MintsMethodSpecsThrough(() => throw new PlatformNotSupportedException()));
        Assert.False(RuntimeCapability.MintsMethodSpecsThrough(() => throw new InvalidOperationException()));
        Assert.Throws<ArgumentNullException>(() => RuntimeCapability.MintsMethodSpecsThrough(null!));
    }

    [Fact]
    public void AnAvailableRuntimeSaysWhetherABodyMayNameAGenericMethod() {
        string mints = RuntimeCapability.Explain(BodyReplacement.Available, null, true);
        string malformed = RuntimeCapability.Explain(BodyReplacement.Available, null, false);

        Assert.Contains("mints a usable MethodSpec", mints);
        Assert.Contains("malformed MethodSpec", malformed);
        Assert.Contains("a target whose body names a generic method is refused", malformed);
        Assert.NotEqual(mints, malformed);
        Assert.Contains(
            "mints a usable MethodSpec",
            RuntimeCapability.Explain(BodyReplacement.PipelineAvailable, null, true));
    }

    [Fact]
    public void AVerdictThatCannotReplaceExplainsTheSameEitherWayTheRuntimeMints() {
        foreach (BodyReplacement support in Enum.GetValues<BodyReplacement>()) {
            if (RuntimeCapability.Replaces(support)) {
                continue;
            }

            Assert.Equal(
                RuntimeCapability.Explain(support, "detail", true),
                RuntimeCapability.Explain(support, "detail", false));
        }
    }

    private static BodyReplacement Support(bool? supported, bool mintingBound, bool pipeline) {
        (BodyReplacement support, string? failure) = RuntimeCapability.Probe(supported, mintingBound, pipeline);
        Assert.Null(failure);
        return support;
    }

    [Fact]
    public void EveryOutcomeExplainsItselfWithoutRepeatingAnother() {
        BodyReplacement[] all = Enum.GetValues<BodyReplacement>();
        List<string> explanations = new List<string>();

        foreach (BodyReplacement support in all) {
            string explained = RuntimeCapability.Explain(support);
            Assert.NotEmpty(explained);
            Assert.DoesNotContain(explained, explanations);
            explanations.Add(explained);
        }

        Assert.Equal(all.Length, explanations.Count);
    }

    [Fact]
    public void ReadingEveryMemberOnARuntimeWithoutTheApiNeitherThrowsNorReportsSupport() {
        bool present = Type.GetType(RuntimeCapability.UpdaterTypeName, throwOnError: false) is not null;

        Assert.Equal(present, RuntimeCapability.Updater() is not null);
        Assert.Equal(
            RuntimeCapability.Support is BodyReplacement.ProbeFailed or BodyReplacement.ApiUnrecognized,
            RuntimeCapability.ProbeFailure is not null);
        Assert.NotEmpty(RuntimeCapability.Runtime);
        Assert.NotEmpty(RuntimeCapability.Explain());
        Assert.Equal(RuntimeCapability.Replaces(RuntimeCapability.Support), RuntimeCapability.IsAvailable);
        Assert.Equal(RuntimeCapability.Support == BodyReplacement.PipelineAvailable, RuntimeCapability.HasPipeline);

        if (present) {
            return;
        }

        Assert.Equal(BodyReplacement.ApiAbsent, RuntimeCapability.Support);
        Assert.False(RuntimeCapability.IsAvailable);
        Assert.False(RuntimeCapability.HasPipeline);
        Assert.Contains(RuntimeCapability.Runtime, RuntimeCapability.Explain());
    }
}

public sealed class SwitchParsingTests {
    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("on")]
    [InlineData("yes")]
    [InlineData("enabled")]
    [InlineData("  on  ")]
    public void EveryOnSpellingReadsAsOn(string raw) {
        Assert.Equal(SwitchState.On, AdapterEntryPoint.ReadSwitch(raw));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("Off")]
    [InlineData("no")]
    [InlineData("disabled")]
    [InlineData("\t0\n")]
    public void EveryOffSpellingReadsAsOff(string raw) {
        Assert.Equal(SwitchState.Off, AdapterEntryPoint.ReadSwitch(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingSetIsNotAChoice(string? raw) {
        Assert.Equal(SwitchState.Unset, AdapterEntryPoint.ReadSwitch(raw));
    }

    [Theory]
    [InlineData("2")]
    [InlineData("offf")]
    [InlineData("waylith")]
    [InlineData("-1")]
    public void AValueNobodyCanReadIsNeitherOnNorOff(string raw) {
        Assert.Equal(SwitchState.Unreadable, AdapterEntryPoint.ReadSwitch(raw));
    }
}

[CollectionDefinition("AdapterStatics", DisableParallelization = true)]
public sealed class AdapterStaticsCollection;

public sealed class ChoiceTests {
    [Fact]
    public void TheInstallLineNamesTheRuntimeAndNothingThatCouldNotHaveBeenOtherwise() {
        Selection chosen = AdapterEntryPoint.Choose(BodyReplacement.PipelineAvailable);

        Assert.True(chosen.Waylith);
        Assert.Equal(
            Selection.Prefix + "backend: body replacement on '" + RuntimeCapability.Runtime + "'.",
            chosen.Message);
    }

    [Fact]
    public void ARuntimeThatCannotStageAComposedBodyGetsNoBackendAndTheLineSaysWhy() {
        foreach (BodyReplacement support in Enum.GetValues<BodyReplacement>()) {
            if (RuntimeCapability.Stages(support)) {
                continue;
            }

            Selection chosen = AdapterEntryPoint.Choose(support);

            Assert.False(chosen.Waylith);
            Assert.Contains("no backend installed", chosen.Message);
            Assert.EndsWith(RuntimeCapability.Explain(support, RuntimeCapability.ProbeFailure) + ".", chosen.Message);
        }
    }

    [Fact]
    public void ARuntimeThatCannotRunItSaysWhichOfTheFiveReasonsItWas() {
        Selection disabled = AdapterEntryPoint.Choose(BodyReplacement.DisabledByRuntime);
        Selection absent = AdapterEntryPoint.Choose(BodyReplacement.ApiAbsent);

        Assert.False(disabled.Waylith);
        Assert.False(absent.Waylith);
        Assert.Contains("DOTNET_MethodBodyUpdater=0", disabled.Reason);
        Assert.NotEqual(absent.Reason, disabled.Reason);
    }
}

public sealed class StandInBackend : IDetourBackend {
    public int PatchedTargetCount { get; init; }

    public IReadOnlyList<string> OwnersOf(MethodBase target) {
        return [];
    }

    public IDetourHandle ApplyComposed(MethodBase target, IReadOnlyList<Injection> added) {
        throw new NotSupportedException();
    }
}

[Collection("AdapterStatics")]
public sealed class InstallTests : IDisposable {
    private readonly IDetourBackend? backendBefore = AmbientSwitches.InstalledBackend();

    public InstallTests() {
        AdapterEntryPoint.ResetForTests();
        AdapterEntryPoint.PatchedTargets = static () => 0;
    }

    public void Dispose() {
        AmbientSwitches.RestoreBackend(backendBefore);
        AdapterEntryPoint.ResetForTests();
        AdapterEntryPoint.PatchedTargets = null;
        Patcher.UseLocalNameResolver(null);
    }

    [Fact]
    public void ASwapAfterTargetsArePatchedIsRefusedAndSaysHowMany() {
        AdapterEntryPoint.PatchedTargets = static () => 7;
        IDetourBackend? previous = AmbientSwitches.InstalledBackend();

        Selection chosen = AdapterEntryPoint.Install(
            _ => {
            },
            [AppContext.BaseDirectory],
            BodyReplacement.PipelineAvailable,
            () => new StandInBackend());

        Assert.False(chosen.Waylith);
        Assert.Same(previous, AmbientSwitches.InstalledBackend());
        Assert.Contains("7 target", chosen.Reason);
        Assert.Contains("already carry a composed body", chosen.Reason);
    }

    [Fact]
    public void TheLateInstallGuardReadsConcordsOwnPatchCount() {
        DetourBackend.Current = new StandInBackend { PatchedTargetCount = 3 };
        IDetourBackend? previous = AmbientSwitches.InstalledBackend();

        AdapterEntryPoint.PatchedTargets = null;

        Selection chosen = AdapterEntryPoint.Install(
            _ => {
            },
            [AppContext.BaseDirectory],
            BodyReplacement.PipelineAvailable,
            () => new StandInBackend());

        Assert.True(Patcher.PatchedTargetCount == 3);
        Assert.False(chosen.Waylith);
        Assert.Same(previous, AmbientSwitches.InstalledBackend());
        Assert.Contains("already carry a composed body", chosen.Reason);
    }

    [Fact]
    public void AnUnsupportedRuntimeLeavesTheBackendExactlyWhereItWas() {
        IDetourBackend? previous = AmbientSwitches.InstalledBackend();
        List<string> written = [];

        Selection chosen = AdapterEntryPoint.Install(
            written.Add,
            [AppContext.BaseDirectory],
            BodyReplacement.ApiAbsent,
            () => new StandInBackend());

        Assert.False(chosen.Waylith);
        Assert.Same(previous, AmbientSwitches.InstalledBackend());
        Assert.StartsWith(Selection.Prefix, Assert.Single(written), StringComparison.Ordinal);
    }

    [Fact]
    public void WithNoSearchDirectoriesTheMissingSymbolPathIsReportedBeforeTheSelection() {
        List<string> written = new List<string>();

        Selection chosen = AdapterEntryPoint.Install(written.Add, null, BodyReplacement.ApiAbsent, null);

        Assert.Equal(2, written.Count);
        Assert.Contains("no assembly directories were supplied", written[0]);
        Assert.Equal(chosen.Message, written[1]);
    }
}

public sealed class LocalNameResolverTests {
    [Fact]
    public void AByteLoadedModuleResolvesToTheFileTheSearchPathHolds() {
        string path = typeof(AdapterEntryPoint).Assembly.Location;
        Assembly loaded = AppDomain.CurrentDomain.Load(File.ReadAllBytes(path));
        Module module = loaded.Modules.First();

        Assert.Empty(loaded.Location);
        Assert.Equal("<Unknown>", module.Name);

        Func<Module, string?> resolver = AdapterEntryPoint.LocalNameResolver([Path.GetDirectoryName(path)!]);

        Assert.Equal(path, resolver(module));
    }

    [Fact]
    public void ADirectoryWithoutTheFileResolvesToNothingRatherThanAGuess() {
        Module module = typeof(AdapterEntryPoint).Assembly.Modules.First();
        Func<Module, string?> resolver = AdapterEntryPoint.LocalNameResolver([Path.GetTempPath()]);

        Assert.Null(resolver(module));
    }

    [Fact]
    public void TheFirstDirectoryHoldingTheFileWins() {
        string path = typeof(AdapterEntryPoint).Assembly.Location;
        Module module = typeof(AdapterEntryPoint).Assembly.Modules.First();
        Func<Module, string?> resolver =
            AdapterEntryPoint.LocalNameResolver([Path.GetTempPath(), Path.GetDirectoryName(path)!]);

        Assert.Equal(path, resolver(module));
    }

    [Fact]
    public void TheCallersArrayCannotBeChangedUnderTheResolver() {
        string path = typeof(AdapterEntryPoint).Assembly.Location;
        string[] directories = [Path.GetDirectoryName(path)!];
        Func<Module, string?> resolver = AdapterEntryPoint.LocalNameResolver(directories);

        directories[0] = Path.GetTempPath();

        Assert.Equal(path, resolver(typeof(AdapterEntryPoint).Assembly.Modules.First()));
        Assert.Throws<ArgumentNullException>(() => AdapterEntryPoint.LocalNameResolver(null!));
    }
}
