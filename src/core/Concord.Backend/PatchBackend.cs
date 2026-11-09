namespace Concord.Backend;

/// <summary>
///     Holds the one patch backend Concord puts composed bodies through for the whole process.
/// </summary>
public static class PatchBackend {
    private static IPatchBackend? current;

    /// <summary>
    ///     The active patch backend. The runtime adapter sets it at install; a test sets its own.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when the assigned backend is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when read before a backend was set.</exception>
    public static IPatchBackend Current {
        get => current ?? throw new InvalidOperationException(
            "No patch backend is installed. The host's loader installs one before anything patches, so either "
            + "that install never ran, or it ran and refused this runtime, or it ran and was switched off. Every "
            + "backend writes one 'backend:' line to the log when it installs, and that line says which of those "
            + "happened; no such line in the log means the install never ran at all.");
        set => current = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    ///     Hands back the installed backend without throwing, for a read-only query that has a sensible answer
    ///     when nothing is installed.
    /// </summary>
    /// <param name="backend">The installed backend, or <see langword="null" /> when there is none.</param>
    /// <returns><see langword="true" /> when a backend is installed.</returns>
    public static bool TryGetCurrent(out IPatchBackend? backend) {
        backend = current;
        return backend is not null;
    }

    internal static void Reset() {
        current = null;
    }
}
