namespace Concord.MonoMod;

/// <summary>Which backend <see cref="MonoModEntryPoint.Install" /> settled on, and why.</summary>
public sealed class Selection {
    internal const string Prefix = "[Concord.MonoMod] ";

    internal Selection(bool installed, string reason, bool dead = false) {
        Installed = installed;
        Reason = reason;
        Dead = dead;
    }

    /// <summary>Whether a backend ended up installed for this process.</summary>
    public bool Installed { get; }

    /// <summary>
    ///     Whether this copy of the backend binds a reloaded, reflection-only 0Harmony and so can never
    ///     install. A host that sees it has nothing to do but report it.
    /// </summary>
    public bool Dead { get; }

    /// <summary>Why this backend was picked, or why this runtime could not run it.</summary>
    public string Reason { get; }

    /// <summary>The single line a host logs after an install.</summary>
    public string Message => Prefix +
        (Installed
            ? "backend: " + Reason + "."
            : "no backend installed, so nothing Concord is asked to patch will be patched. " + Reason);
}
