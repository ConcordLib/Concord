using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Concord.Host.Adapter.ForkTests;

public readonly struct WatchedPair {
    public WatchedPair(int original, int replaced) {
        Original = original;
        Replaced = replaced;
    }

    public int Original { get; }

    public int Replaced { get; }
}

public static class WatchingUpdater {
    public static MethodBase? Watched { get; set; }

    public static InstalledBody Seen { get; set; } = (InstalledBody)(-1);

    public static void ReplaceMethodBody(MethodBase target, ReadOnlySpan<byte> body, ReadOnlySpan<WatchedPair> map) {
        if (Watched is not null && target == Watched) {
            Seen = WaylithDetourBackend.Writers.Installed(target);
        }
    }

    public static void Revert(MethodBase target) {
    }
}

public static class Ordered {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Value() {
        return 3;
    }

    public static void Tail() {
    }
}

public sealed class PublishOrderTests : IDisposable {
    private const string NoFork = "No host body read here, so the registry cannot answer what is installed.";

    private static readonly MethodBase Target = typeof(Ordered).GetMethod(nameof(Ordered.Value))!;

    public static bool Unavailable => ForkRuntime.Unavailable || !ForkRuntime.HostRead;

    public void Dispose() {
        WatchingUpdater.Watched = null;
        WaylithDetourBackend.Writers.Release(Target);
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void TheRegistryAlreadyCarriesConcordsIlWhenTheWriteGoesIn() {
        MethodInfo injection = typeof(Ordered).GetMethod(nameof(Ordered.Tail))!;
        RefusingBackend fallback = new RefusingBackend();
        ComposedBodyPublisher publisher =
            new ComposedBodyPublisher(fallback, new ForkBodyWriter(typeof(WatchingUpdater)));
        WatchingUpdater.Watched = Target;

        publisher.PublishComposed(Target, [new Injection(injection, new InjectAt.Head(), "publish-order", 0)]);

        Assert.True(fallback.HandedOff.Count == 0, "the publisher conceded the target, so no write was attempted");
        Assert.True(
            WatchingUpdater.Seen == InstalledBody.Replaced,
            $"the registry answered {WatchingUpdater.Seen} about the target while Concord's own write was going in, " +
            "where Replaced is the record present and the body not in yet. Unknown means it had no record of the IL " +
            "it was about to install, so a reader landing inside the write could not tell Concord's body from a " +
            "foreign one. The record has to go in before the write.");
    }
}
