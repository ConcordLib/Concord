using System.Reflection;
using Xunit;

namespace Concord.MonoMod.Tests;

public sealed class ReplaceInnerTests {
    [Fact]
    public void ReplaceInner_ForwardsToTheNewBackend() {
        MonoModBackend first = new MonoModBackend();
        MonoModBackend second = new MonoModBackend();
        RoutingBackend router = new RoutingBackend(first, _ => { });

        router.ReplaceInner(second);

        Assert.Same(second, router.Inner);
    }

    [Fact]
    public void ReplaceInner_RefusesNull() {
        RoutingBackend router = new RoutingBackend(new MonoModBackend(), _ => { });

        Assert.Throws<ArgumentNullException>(() => router.ReplaceInner(null!));
    }

    public static int Subject(int seed) {
        return seed + 1;
    }
}
