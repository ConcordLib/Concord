using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Backend;
using Concord.Emit;
using Xunit;

namespace Concord.MonoMod.Tests;

public static class OwnedBodyTargets {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Raw() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Routed() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Untouched() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Released() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Lost() {
        return 1;
    }
}

public static class OwnedBodyInjections {
    public static void Bump(ControlHandle<int> handle) {
        handle.ReturnValue += 1;
    }
}

public sealed class OwnedBodyQueryTests {
    [Fact]
    public void OwnsBody_RawDetour_IsTrue() {
        MethodBase target = Target(nameof(OwnedBodyTargets.Raw));

        using IBackendHandle handle = new MonoModBackend().ApplyComposed(target, [Bump("A")]);

        Assert.True(TargetDetourRegistry.OwnsBody(target));
    }

    [Fact]
    public void OwnsBody_AfterTargetRoutedToForeignHost_IsFalse() {
        MethodBase target = Target(nameof(OwnedBodyTargets.Routed));
        RoutingBackend router = new RoutingBackend(new MonoModBackend(), _ => { });
        StubHost host = new StubHost();
        router.ActivateHost(host);

        try {
            using IBackendHandle handle = router.ApplyComposed(target, [Bump("A")]);
            Assert.True(TargetDetourRegistry.OwnsBody(target));

            router.OnForeignPatchPending(target, new object());
            router.OnForeignPatchCommitted(target);

            Assert.Equal(RouteState.HarmonyOwned, router.GetRoute(target));
            Assert.NotEmpty(PatchOwners.Of(target));
            Assert.False(TargetDetourRegistry.OwnsBody(target));
        } finally {
            PatchOwners.Host = null;
        }
    }

    [Fact]
    public void OwnsBody_NoInjections_IsFalse() {
        Assert.False(TargetDetourRegistry.OwnsBody(Target(nameof(OwnedBodyTargets.Untouched))));
    }

    [Fact]
    public void OwnsBody_DisposedHandle_IsFalse() {
        MethodBase target = Target(nameof(OwnedBodyTargets.Released));
        IBackendHandle handle = new MonoModBackend().ApplyComposed(target, [Bump("A")]);

        handle.Dispose();

        Assert.False(TargetDetourRegistry.OwnsBody(target));
    }

    private static MethodBase Target(string name) {
        return typeof(OwnedBodyTargets).GetMethod(name)!;
    }

    private static Injection Bump(string owner) {
        MethodInfo body = typeof(OwnedBodyInjections).GetMethod(nameof(OwnedBodyInjections.Bump))!;
        return new Injection(body, new InjectAt.Tail(), owner, 0);
    }

    private sealed class StubHost : IForeignPatchHost {
        public IDisposable? EnterHostLock() {
            return null;
        }

        public bool HasPatch(MethodBase target) {
            return false;
        }

        public bool TryRebuild(MethodBase target, out string refusals) {
            refusals = string.Empty;
            return true;
        }

        public IReadOnlyList<string> ForeignOwners(MethodBase target) {
            return [];
        }

        public IReadOnlyList<string> ConcordOwners(MethodBase target) {
            return [];
        }

        public bool TryInstallNotifier(IForeignPatchObserver observer, MonoModBackend rawBackend) {
            return true;
        }

        private sealed class StubHandle(MethodBase original) : IBackendHandle {
            public MethodBase Original { get; } = original;

            public bool IsApplied { get; private set; } = true;

            public void Dispose() {
                IsApplied = false;
            }
        }
    }
}
