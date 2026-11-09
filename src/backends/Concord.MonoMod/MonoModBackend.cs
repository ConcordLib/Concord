using System.Reflection;
using System.Threading;
using Concord.Backend;
using Concord.Emit;
using MonoMod.Core;

namespace Concord.MonoMod;

/// <summary>
///     Detour backend that delegates native method redirection to MonoMod.Core, the same engine Harmony
///     uses. MonoMod.Core resolves the runtime generic context, so methods declared on a generic type are
///     redirected correctly.
/// </summary>
public sealed class MonoModBackend : IPatchBackend {
    /// <summary>
    ///     Initializes a new instance of the <see cref="MonoModBackend" /> class and wires
    ///     <see cref="ReversePatchFactory.OwnsBody" /> to it, so <see cref="ReversePatchFactory.Bind" />
    ///     can tell Concord-owned bodies apart from untouched ones without a host doing it by hand.
    /// </summary>
    public MonoModBackend() {
        ReversePatchFactory.OwnsBody = OwnsBody;
    }

    /// <inheritdoc />
    public string Name => RoutingBackend.BackendName;

    /// <inheritdoc />
    public int PatchedTargetCount => TargetDetourRegistry.PatchedTargetCount;

    /// <inheritdoc />
    public TargetVerdict Judge(MethodBase target) {
        if (target is null) {
            throw new ArgumentNullException(nameof(target));
        }

        try {
            WrapperComposer.RejectSharedGenericInstantiation(target);
        } catch (ConcordEmitException refused) {
            return TargetVerdict.Refuse(refused.Code, refused.Message);
        }

        return TargetVerdict.Accept();
    }

    /// <inheritdoc />
    public IReadOnlyList<string> OwnersOf(MethodBase target) {
        if (target is null) {
            throw new ArgumentNullException(nameof(target));
        }

        return TargetDetourRegistry.OwnersFor(target);
    }

    /// <inheritdoc />
    public bool OwnsBody(MethodBase target) {
        return TargetDetourRegistry.OwnsBody(target);
    }

    /// <inheritdoc />
    public bool Recompose(MethodBase target) {
        if (target is null) {
            throw new ArgumentNullException(nameof(target));
        }

        return TargetDetourRegistry.RecomposeLive(target);
    }

    /// <summary>
    ///     Redirects one method to a replacement, composing nothing.
    /// </summary>
    /// <param name="original">The method to redirect.</param>
    /// <param name="replacement">The method to run instead.</param>
    /// <returns>A handle that restores <paramref name="original" /> when disposed.</returns>
    public IBackendHandle Apply(MethodBase original, MethodInfo replacement) {
        if (original is null) {
            throw new ArgumentNullException(nameof(original));
        }

        if (replacement is null) {
            throw new ArgumentNullException(nameof(replacement));
        }

        ICoreDetour detour = MonoModHost.Factory.CreateDetour(original, replacement);
        return new Handle(original, detour);
    }

    /// <inheritdoc />
    public IBackendHandle ApplyComposed(MethodBase target, IReadOnlyList<Injection> added) {
        if (target is null) {
            throw new ArgumentNullException(nameof(target));
        }

        if (added is null) {
            throw new ArgumentNullException(nameof(added));
        }

        return TargetDetourRegistry.Add(target, added);
    }

    private sealed class Handle(MethodBase original, ICoreDetour detour) : IBackendHandle {
        private ICoreDetour? _detour = detour;

        public MethodBase Original { get; } = original;

        public bool IsApplied => Volatile.Read(ref _detour) is { IsApplied: true };

        public void Dispose() {
            ICoreDetour? d = Interlocked.Exchange(ref _detour, null);
            if (d is null) {
                return;
            }

            if (d.IsApplied) {
                d.Undo();
            }

            d.Dispose();
        }
    }
}
