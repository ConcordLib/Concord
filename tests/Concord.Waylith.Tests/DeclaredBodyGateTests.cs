using System.Reflection;
using Concord.Backend;
using Concord.Emit;
using Xunit;

namespace Concord.Waylith.Tests;

[Collection("AdapterStatics")]
public sealed class DeclaredBodyGateTests : IDisposable {
    private readonly IPatchBackend? backendBefore = AmbientSwitches.InstalledBackend();

    private readonly Func<MethodBase, ComposedBody?>? readerBefore = OriginalBody.HostOriginalBody;

    private readonly Action<string> logBefore = WaylithClrBackend.Log;

    public DeclaredBodyGateTests() {
        AdapterEntryPoint.ResetForTests();
        AdapterEntryPoint.PatchedTargets = static () => 0;
    }

    public void Dispose() {
        AdapterEntryPoint.PatchedTargets = null;
        AdapterEntryPoint.DeclaredBodyReads = null;
        OriginalBody.HostOriginalBody = readerBefore;
        WaylithClrBackend.Log = logBefore;
        AmbientSwitches.RestoreBackend(backendBefore);
        AdapterEntryPoint.ResetForTests();
    }

    [Fact]
    public void ARuntimeThatReplacesBodiesButWillNotReadADeclaredOneBackDoesNotGetTheBackend() {
        AdapterEntryPoint.DeclaredBodyReads = static () => false;
        IPatchBackend? previous = AmbientSwitches.InstalledBackend();

        Selection chosen = AdapterEntryPoint.Install(
            static _ => {
            },
            [AppContext.BaseDirectory],
            BodyReplacement.PipelineAvailable,
            static () => new StandInBackend());

        Assert.True(
            !chosen.Waylith && ReferenceEquals(previous, AmbientSwitches.InstalledBackend()),
            $"the swap installed body replacement on a runtime whose declared-body read did not bind: " +
            $"waylith={chosen.Waylith}, backend swapped=" +
            $"{!ReferenceEquals(previous, AmbientSwitches.InstalledBackend())}. " +
            "Writing bodies without that read means every original-body read returns the body Concord last " +
            "wrote, so the install has to refuse instead of reporting success.");
        Assert.Contains("will not read a declared one back", chosen.Reason, StringComparison.Ordinal);
    }
}
