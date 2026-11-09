using System.Reflection;
using Concord.Backend;
using Xunit;

namespace Concord.MonoMod.Tests;

public class MonoModBackendTests {
    private static readonly MethodInfo Src = typeof(Targets).GetMethod(nameof(Targets.OriginalC))!;
    private static readonly MethodInfo Dst = typeof(Targets).GetMethod(nameof(Targets.ReplacementC))!;

    [Fact]
    public void Apply_redirects_the_original() {
        MonoModBackend backend = new MonoModBackend();
        Assert.Equal(100, Targets.OriginalC());

        using IBackendHandle handle = backend.Apply(Src, Dst);

        Assert.True(handle.IsApplied);
        Assert.Same(Src, handle.Original);
        Assert.Equal(200, Targets.OriginalC());
    }

    [Fact]
    public void Dispose_restores_the_original() {
        MonoModBackend backend = new MonoModBackend();
        IBackendHandle handle = backend.Apply(Src, Dst);
        Assert.Equal(200, Targets.OriginalC());

        handle.Dispose();

        Assert.False(handle.IsApplied);
        Assert.Equal(100, Targets.OriginalC());
    }

    [Fact]
    public void Double_dispose_is_safe_and_stays_restored() {
        MonoModBackend backend = new MonoModBackend();
        IBackendHandle handle = backend.Apply(Src, Dst);

        handle.Dispose();
        handle.Dispose();

        Assert.False(handle.IsApplied);
        Assert.Equal(100, Targets.OriginalC());
    }
}
