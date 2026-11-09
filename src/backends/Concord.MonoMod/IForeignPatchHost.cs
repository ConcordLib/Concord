using System.Reflection;

namespace Concord.MonoMod;

/// <summary>
///     A foreign patching library that Concord shares a method with. The host keeps the entry point of
///     every method it patches and builds its wrapper over the body Concord composed.
/// </summary>
public interface IForeignPatchHost {
    /// <summary>
    ///     Enters the patching lock of every armed copy of the host library, in arming order. The router
    ///     takes its own gate only inside that scope, so the two never deadlock. Reentrant on one thread.
    /// </summary>
    /// <returns>A scope that releases the locks when disposed, or null when the host has no reachable lock.</returns>
    IDisposable? EnterHostLock();

    /// <summary>
    ///     Reports whether the host already has a patch on a target, so the host owns its entry point.
    /// </summary>
    /// <param name="target">The method to test.</param>
    /// <returns>True when any loaded copy of the host library has a patch on it.</returns>
    bool HasPatch(MethodBase target);

    /// <summary>
    ///     Asks the host to rebuild a method it owns, so its wrapper picks up Concord's current body. The
    ///     host resolves the instantiation it patched when one body is shared across generics.
    /// </summary>
    /// <param name="target">The method to rebuild, already normalized.</param>
    /// <param name="refusals">Why the host did not rebuild, empty when it did.</param>
    /// <returns>True when the host rebuilt the method.</returns>
    bool TryRebuild(MethodBase target, out string refusals);

    /// <summary>
    ///     Lists the identifiers of foreign patch owners on a target, ignoring Concord's own.
    /// </summary>
    /// <param name="target">The method to inspect.</param>
    /// <returns>The foreign owner identifiers, empty when none.</returns>
    IReadOnlyList<string> ForeignOwners(MethodBase target);

    /// <summary>
    ///     Lists Concord's own patch owners on a target the host owns. Those injections reach the method
    ///     through the host's wrapper rather than Concord's detour registry.
    /// </summary>
    /// <param name="target">The method to inspect.</param>
    /// <returns>The Concord owner identifiers in run order, empty when none.</returns>
    IReadOnlyList<string> ConcordOwners(MethodBase target);

    /// <summary>
    ///     Hooks the host so it notifies <paramref name="observer" /> before it rebuilds any method, and
    ///     so its wrapper build reads Concord's composed body.
    /// </summary>
    /// <param name="observer">The observer to notify.</param>
    /// <param name="rawBackend">The plain backend to install the hooks with, never the routing one.</param>
    /// <returns>True when the notifier installed.</returns>
    bool TryInstallNotifier(IForeignPatchObserver observer, MonoModBackend rawBackend);
}
