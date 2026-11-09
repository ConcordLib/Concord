using System.Reflection;

namespace Concord.MonoMod;

/// <summary>
///     Receives notice from a foreign patch host that a method is about to be rebuilt, so Concord can
///     move a target it already detoured onto the host before the host takes the entry point.
/// </summary>
public interface IForeignPatchObserver {
    /// <summary>
    ///     Called while the host holds its own lock, just before it rebuilds <paramref name="target" />,
    ///     and provisional: a rebuild that throws leaves the target with Concord. Never throw from here.
    /// </summary>
    /// <param name="target">The method the host is about to rebuild.</param>
    /// <param name="hostPatchState">
    ///     The host's own patch record for this rebuild, opaque to Concord. Concord must contribute
    ///     through this object rather than by calling back into the host: the host applies and stores this
    ///     record after the rebuild returns, so anything registered by a nested call is overwritten.
    /// </param>
    void OnForeignPatchPending(MethodBase target, object hostPatchState);
}
