using System.Reflection;

namespace Concord.Backend;

/// <summary>
///     Represents a composed body a backend applied to one method.
/// </summary>
public interface IBackendHandle : IDisposable {
    /// <summary>
    ///     Gets the method this handle's injections were applied to.
    /// </summary>
    MethodBase Original { get; }

    /// <summary>
    ///     Gets a value indicating whether this handle's injections are currently live.
    /// </summary>
    bool IsApplied { get; }
}
