using System.Reflection;
using Concord.Detour;
using Concord.Emit;
using Xunit;

namespace Concord.Host.Adapter.Tests;

public static class GatedSubject {
    public static int Value() {
        return 1;
    }

    public static void Bump() {
    }
}

[Collection("AdapterStatics")]
public sealed class DeclaredBodyGateTests : IDisposable {
    private readonly IDetourBackend backendBefore = DetourBackend.Current;

    private readonly Func<MethodBase, ComposedBody?>? readerBefore = OriginalBody.HostOriginalBody;

    private readonly Action<string> logBefore = WaylithDetourBackend.Log;

    public DeclaredBodyGateTests() {
        AdapterEntryPoint.ResetForTests();
        AdapterEntryPoint.PatchedTargets = static () => 0;
    }

    public void Dispose() {
        AdapterEntryPoint.PatchedTargets = null;
        AdapterEntryPoint.DeclaredBodyReads = null;
        OriginalBody.HostOriginalBody = readerBefore;
        WaylithDetourBackend.Log = logBefore;
        DetourBackend.Current = backendBefore;
        AdapterEntryPoint.ResetForTests();
    }

    [Fact]
    public void ARuntimeThatReplacesBodiesButWillNotReadADeclaredOneBackDoesNotGetTheBackend() {
        AdapterEntryPoint.DeclaredBodyReads = static () => false;
        IDetourBackend previous = DetourBackend.Current;

        Selection chosen = AdapterEntryPoint.Install(
            static _ => {
            },
            null,
            [AppContext.BaseDirectory],
            BodyReplacement.Available,
            static _ => new StandInBackend());

        Assert.True(
            !chosen.Waylith && ReferenceEquals(previous, DetourBackend.Current),
            $"the swap installed body replacement on a runtime whose declared-body read did not bind: " +
            $"waylith={chosen.Waylith}, backend swapped={!ReferenceEquals(previous, DetourBackend.Current)}. " +
            "Writing bodies without that read means every original-body read returns the body Concord last " +
            "wrote, so the install has to refuse instead of reporting success.");
        Assert.Contains("will not read a declared one back", chosen.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ADeclaredBodyRefusalIsNamedOnceInTheLogAndStillStopsThePatch() {
        MethodBase target = typeof(GatedSubject).GetMethod(nameof(GatedSubject.Value))!;
        MethodInfo injection = typeof(GatedSubject).GetMethod(nameof(GatedSubject.Bump))!;
        Injection[] ordered = [new Injection(injection, new InjectAt.Head(), "declared-body-gate", 0)];
        List<string> written = [];
        WaylithDetourBackend.Log = written.Add;
        OriginalBody.HostOriginalBody = static _ =>
            throw new ConcordEmitException("CONC178", "this host would not read its declared body back");
        ComposedBodyPublisher publisher = new ComposedBodyPublisher(new MonoModDetourBackend());

        ConcordEmitException refused =
            Assert.Throws<ConcordEmitException>(() => publisher.PublishComposed(target, ordered));
        Assert.Throws<ConcordEmitException>(() => publisher.PublishComposed(target, ordered));

        List<string> named = written.FindAll(line => line.Contains(TargetEligibility.RefusalMarker, StringComparison.Ordinal));

        Assert.Equal("CONC178", refused.Code);
        Assert.True(
            named.Count == 1,
            $"two publish attempts on a target whose declared body the host refuses logged {named.Count} refusal " +
            "lines. The refusal is fatal on both the composed path and the detour path, so it has to be named " +
            "once per target rather than stay silent or repeat on every attempt.");
        Assert.Contains("cannot stand in for it", named[0], StringComparison.Ordinal);
    }
}
