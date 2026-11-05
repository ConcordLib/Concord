namespace Concord.Detour;

/// <summary>
///     Holds the one detour backend Concord puts composed bodies through for the whole process.
/// </summary>
public static class DetourBackend {
    private static IDetourBackend? current;

    /// <summary>
    ///     The active detour backend. The runtime adapter sets it at install; a test sets its own.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when the assigned backend is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when read before a backend was set.</exception>
    public static IDetourBackend Current {
        get => current ?? throw new InvalidOperationException(
            "No detour backend is installed. Either the host never called AdapterEntryPoint.Install before "
            + "anything patched, or Install ran and refused this runtime or was switched off. Concord needs "
            + "WaylithCLR 10.0.15; the '[Concord.Waylith] backend:' line in the log says which of the two it is, "
            + "and no such line means Install never ran.");
        set => current = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    ///     Hands back the installed backend without throwing, for a read-only query that has a sensible answer
    ///     when nothing is installed.
    /// </summary>
    /// <param name="backend">The installed backend, or <see langword="null" /> when there is none.</param>
    /// <returns><see langword="true" /> when a backend is installed.</returns>
    public static bool TryGetCurrent(out IDetourBackend? backend) {
        backend = current;
        return backend is not null;
    }

    internal static void Reset() {
        current = null;
    }
}
