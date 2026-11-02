using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Detour;
using Xunit;

namespace Concord.Host.Adapter.ForkTests;

public static class Forwarded {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Handed() {
        return 1;
    }

    public static int Instead() {
        return 9;
    }
}

public sealed class ForwarderClaimTests : IDisposable {
    private const string NoFork = "No host body read here, so no installed body can be compared.";

    private static readonly MethodInfo Target = typeof(Forwarded).GetMethod(nameof(Forwarded.Handed))!;

    private IDetourHandle? handle;

    public static bool Unavailable => ForkRuntime.Unavailable || !ForkRuntime.HostRead;

    public void Dispose() {
        handle?.Dispose();
        if (WaylithDetourBackend.Writers.HoldsClaim(Target)) {
            ForkRuntime.Restore(Target);
            WaylithDetourBackend.Writers.Release(Target);
        }
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void AForwarderBodyIsClaimedWellEnoughToSeeSomeoneElseReplaceIt() {
        RefusingBackend fallback = new RefusingBackend();
        WaylithDetourBackend backend = new WaylithDetourBackend(
            fallback, new ComposedBodyPublisher(fallback), ForkBodyWriter.Shared);

        Assert.Equal(InstalledBody.Unknown, WaylithDetourBackend.Writers.Installed(Target));

        handle = backend.Apply(Target, typeof(Forwarded).GetMethod(nameof(Forwarded.Instead))!);

        Assert.True(handle.IsApplied, "the forwarder did not apply, so nothing was published to compare");
        Assert.Equal(9, Forwarded.Handed());
        Assert.Equal(InstalledBody.Concords, WaylithDetourBackend.Writers.Installed(Target));

        ForkRuntime.Write(Target, 5);

        InstalledBody verdict = WaylithDetourBackend.Writers.Installed(Target);

        Assert.True(
            verdict == InstalledBody.Replaced,
            $"someone wrote a body over the forwarder Concord published and the registry reports {verdict}. A " +
            "forwarder is a target Concord published to, which is exactly what WriterRegistry.BlindSpot says a " +
            "replacement is visible on, so the forwarder path has to record its IL like the composed path does.");
    }
}
