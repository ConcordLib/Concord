using System.Reflection;
using Concord.Backend;
using Concord.Emit;
using Concord.MonoMod.Harmony;

namespace Concord.MonoMod;

/// <summary>
///     A detour backend that keeps Concord's injections running whether or not a foreign patcher owns
///     the method, by feeding one injection table to both its own detour and the host's wrapper build.
/// </summary>
public sealed class RoutingBackend : IPatchBackend, IForeignPatchObserver {
    /// <summary>The name every backend in this package reports in a log line.</summary>
    public const string BackendName = "Concord.MonoMod";

    private readonly object gate = new object();
    private readonly Action<string> log;
    private readonly HashSet<MethodBase> hostOwned = [];
    private readonly HashSet<MethodBase> pending = [];
    private MonoModBackend inner;
    private volatile IForeignPatchHost? host;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RoutingBackend" /> class.
    /// </summary>
    /// <param name="inner">The backend used for targets Concord detours itself.</param>
    /// <param name="log">Writes coexistence messages to the host game's log.</param>
    public RoutingBackend(MonoModBackend inner, Action<string> log) {
        this.inner = inner;
        this.log = log;
    }

    /// <summary>
    ///     The backend this router forwards to. A host that wires more than once per process, such as
    ///     RimWorld under an assembly reloader, wraps this instead of the previous router.
    /// </summary>
    public MonoModBackend Inner => inner;

    /// <inheritdoc />
    public string Name => BackendName;

    /// <inheritdoc />
    public int PatchedTargetCount => inner.PatchedTargetCount;

    /// <summary>
    ///     Whether the host's notifier hook is installed. When false, a foreign patch arriving on a
    ///     target Concord already detoured is not seen until the next apply on it.
    /// </summary>
    public bool NotifierInstalled { get; private set; }

    /// <summary>
    ///     Rebinds what this router forwards to, keeping every route and the host notifier. A backend
    ///     installing late swaps here rather than replacing the router, which would drop all of it.
    /// </summary>
    /// <param name="backend">The backend to forward to.</param>
    public void ReplaceInner(MonoModBackend backend) {
        if (backend is null) {
            throw new ArgumentNullException(nameof(backend));
        }

        lock (gate) {
            inner = backend;
        }
    }

    /// <summary>
    ///     Attaches the foreign patch host and installs its hooks. Only the first call takes effect.
    /// </summary>
    /// <param name="host">The host whose wrapper Concord's body rides inside.</param>
    public void ActivateHost(IForeignPatchHost host) {
        lock (gate) {
            if (this.host != null) {
                return;
            }

            this.host = host;
            PatchOwners.Host = host;
        }

        try {
            NotifierInstalled = host.TryInstallNotifier(this, inner);
        } catch (Exception ex) {
            NotifierInstalled = false;
            log(CoexistenceLogMarkers.HookUnavailable + " " + ExceptionReason.Of(ex));
        }
    }

    /// <inheritdoc />
    public TargetVerdict Judge(MethodBase target) {
        if (target is null) {
            throw new ArgumentNullException(nameof(target));
        }

        return inner.Judge(target);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> OwnersOf(MethodBase target) {
        if (target is null) {
            throw new ArgumentNullException(nameof(target));
        }

        return PatchOwners.Of(target);
    }

    /// <inheritdoc />
    public bool OwnsBody(MethodBase target) {
        return inner.OwnsBody(target);
    }

    /// <inheritdoc />
    public bool Recompose(MethodBase target) {
        MethodBase normalized = MethodIdentity.Normalize(target);
        MethodBase routeKey = MethodIdentity.SharedBodyKey(normalized);

        using (EnterHostLockBeforeGate()) {
            lock (gate) {
                if (!IsHostOwned(routeKey, normalized)) {
                    return inner.Recompose(target);
                }

                return TryRebuild(normalized, out _);
            }
        }
    }

    /// <summary>
    ///     Redirects one method to a replacement without composing, for a target no foreign host owns.
    /// </summary>
    /// <param name="original">The method to redirect.</param>
    /// <param name="replacement">The method to run instead.</param>
    /// <returns>A handle that restores <paramref name="original" /> when disposed.</returns>
    public IBackendHandle Apply(MethodBase original, MethodInfo replacement) {
        original = MethodIdentity.Normalize(original);
        MethodBase routeKey = MethodIdentity.SharedBodyKey(original);

        using (EnterHostLockBeforeGate()) {
            lock (gate) {
                if (IsHostOwned(routeKey, original)) {
                    throw new InvalidOperationException(
                        "Concord.MonoMod.RoutingBackend.Apply replaces a whole method and cannot be used on a target a foreign patcher owns: " +
                        DescribeTarget(original));
                }

                return inner.Apply(original, replacement);
            }
        }
    }

    /// <inheritdoc />
    public IBackendHandle ApplyComposed(MethodBase target, IReadOnlyList<Injection> added) {
        target = MethodIdentity.Normalize(target);
        added = WrapperComposer.TagRequestedInstantiation(target, added);
        MethodBase routeKey = MethodIdentity.SharedBodyKey(target);

        using (EnterHostLockBeforeGate()) {
            lock (gate) {
                long[] fed = TranspilerParticipant.Registry.Add(target, added);
                IBackendHandle? raw = null;

                try {
                    bool owned = IsHostOwned(routeKey, target);
                    raw = inner.ApplyComposed(target, added);

                    if (owned && !TryRebuild(target, out string refusals)) {
                        throw RebuildRefused(target, refusals);
                    }
                } catch {
                    TranspilerParticipant.Registry.Remove(target, fed);
                    raw?.Dispose();
                    throw;
                }

                return new RouteHandle(this, target, routeKey, fed, raw);
            }
        }
    }

    /// <inheritdoc />
    public void OnForeignPatchPending(MethodBase target, object hostPatchState) {
        try {
            MethodBase routeKey = MethodIdentity.SharedBodyKey(MethodIdentity.Normalize(target));

            lock (gate) {
                if (!hostOwned.Contains(routeKey)) {
                    pending.Add(routeKey);
                }
            }
        } catch (Exception ex) {
            log(CoexistenceLogMarkers.HostOwned + " notifier failed for " + DescribeTarget(target) + ": " + ExceptionReason.Of(ex));
        }
    }

    /// <summary>
    ///     Reports who owns a target's entry point.
    /// </summary>
    /// <param name="target">The method to inspect.</param>
    /// <returns>The target's state.</returns>
    public RouteState GetRoute(MethodBase target) {
        MethodBase normalized = MethodIdentity.Normalize(target);
        MethodBase routeKey = MethodIdentity.SharedBodyKey(normalized);

        using (EnterHostLockBeforeGate()) {
            lock (gate) {
                return IsHostOwned(routeKey, normalized) ? RouteState.HarmonyOwned : RouteState.Raw;
            }
        }
    }

    internal void OnForeignPatchCommitted(MethodBase target) {
        try {
            MethodBase normalized = MethodIdentity.Normalize(target);
            MethodBase routeKey = MethodIdentity.SharedBodyKey(normalized);

            lock (gate) {
                if (!pending.Remove(routeKey) || !hostOwned.Add(routeKey)) {
                    return;
                }
            }

            TargetDetourRegistry.Supersede(routeKey);

            if (TranspilerParticipant.Registry.HasInjections(normalized)) {
                log(CoexistenceLogMarkers.HostOwned + " " + DescribeTarget(normalized));
            }
        } catch (Exception ex) {
            log(CoexistenceLogMarkers.HostOwned + " notifier failed for " + DescribeTarget(target) + ": " + ExceptionReason.Of(ex));
        }
    }

    private static string DescribeTarget(MethodBase target) {
        return (target.DeclaringType?.Name ?? "<module>") + "." + target.Name;
    }

    private static ConcordEmitException RebuildRefused(MethodBase target, string refusals) {
        return new ConcordEmitException(
            "CONC186",
            $"A foreign patch host would not rebuild its wrapper for '{target.DeclaringType?.FullName}.{target.Name}', so this change was undone. " +
            "The wrapper already at the method keeps running with Concord's last composed body inside it. The host's reason: " + refusals);
    }

    private IDisposable? EnterHostLockBeforeGate() {
        IForeignPatchHost? current = host;
        return current?.EnterHostLock();
    }

    private bool TryRebuild(MethodBase target, out string refusals) {
        IForeignPatchHost active = host!;

        if (active.TryRebuild(target, out refusals)) {
            return true;
        }

        log(CoexistenceLogMarkers.RebuildRefused + " " + DescribeTarget(target) + ": " + refusals);
        return false;
    }

    private bool IsHostOwned(MethodBase routeKey, MethodBase target) {
        if (hostOwned.Contains(routeKey)) {
            return true;
        }

        IForeignPatchHost? active = host;
        if (active == null || !active.HasPatch(target)) {
            return false;
        }

        hostOwned.Add(routeKey);
        TargetDetourRegistry.Supersede(routeKey);
        return true;
    }

    private sealed class RouteHandle : IBackendHandle {
        private readonly RoutingBackend owner;
        private readonly MethodBase target;
        private readonly MethodBase routeKey;
        private readonly long[] fed;
        private readonly IBackendHandle? raw;
        private bool disposed;

        public RouteHandle(RoutingBackend owner, MethodBase target, MethodBase routeKey, long[] fed, IBackendHandle? raw) {
            this.owner = owner;
            this.target = target;
            this.routeKey = routeKey;
            this.fed = fed;
            this.raw = raw;
        }

        public MethodBase Original => target;

        public bool IsApplied {
            get {
                if (disposed) {
                    return false;
                }

                lock (owner.gate) {
                    return owner.hostOwned.Contains(routeKey) || raw is { IsApplied: true };
                }
            }
        }

        public void Dispose() {
            using (owner.EnterHostLockBeforeGate()) {
                lock (owner.gate) {
                    if (disposed) {
                        return;
                    }

                    (long Seq, Injection Injection)[] removed = TranspilerParticipant.Registry.Remove(target, fed);

                    try {
                        if (owner.IsHostOwned(routeKey, target) && !owner.TryRebuild(target, out string refusals)) {
                            throw RebuildRefused(target, refusals);
                        }
                    } catch {
                        TranspilerParticipant.Registry.Restore(target, removed);
                        throw;
                    }

                    disposed = true;
                    raw?.Dispose();
                }
            }
        }
    }
}
