using Concord.Backend;

namespace Concord;

/// <summary>
///     A disposable handle over one or more applied patches. Disposing reverts every patch the handle owns.
/// </summary>
public interface IPatchHandle : IDisposable {
    /// <summary>
    ///     Gets a value indicating whether the handle's patches are currently applied.
    ///     Returns true only while the handle is undisposed and all underlying handles are applied.
    /// </summary>
    bool IsApplied { get; }

    /// <summary>
    ///     Gets the underlying detour handles, so callers can inspect or revert them individually.
    /// </summary>
    IReadOnlyList<IBackendHandle> Detours { get; }
}
