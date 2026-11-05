using System.Reflection;

namespace Concord.Host.Adapter;

/// <summary>
///     Reads a method's original IL back from the host, bypassing <see cref="MethodBase.GetMethodBody" />, which on
///     a runtime that replaces bodies in place reports whatever body is live rather than the declared one.
/// </summary>
public sealed class ForkBodyReader {
    private static readonly bool Bound = RuntimeCapability.Updater() is not null;

    /// <summary>The process-wide reader, so every caller shares one availability check.</summary>
    public static ForkBodyReader Shared { get; } = new ForkBodyReader();

    /// <summary><see langword="true" /> when this runtime ships the read. <see langword="false" /> off the fork.</summary>
    public bool IsAvailable => Bound;

    /// <summary>Reads <paramref name="target" />'s original body, the one its metadata declares.</summary>
    /// <param name="target">The method to read.</param>
    /// <returns>The body, or <see langword="null" /> when this runtime has no such read or the host refused one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target" /> is <see langword="null" />.</exception>
    public HostBody? Read(MethodBase target) {
        ArgumentNullException.ThrowIfNull(target);
        return Bound ? ForkApi.ReadDefault(target) : null;
    }

    /// <summary>Reads the body currently installed on <paramref name="target" />, replacements included.</summary>
    /// <param name="target">The method to read.</param>
    /// <returns>The body, or <see langword="null" /> when this runtime has no such read or the host refused one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target" /> is <see langword="null" />.</exception>
    public HostBody? ReadActive(MethodBase target) {
        ArgumentNullException.ThrowIfNull(target);
        return Bound ? ForkApi.ReadCurrent(target) : null;
    }
}
