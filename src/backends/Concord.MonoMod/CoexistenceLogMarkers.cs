namespace Concord.MonoMod;

/// <summary>
///     Stable log prefixes for coexistence events, so players and bug reports can grep one string.
/// </summary>
public static class CoexistenceLogMarkers {
    /// <summary>
    ///     A foreign patch host took the target's entry point, so its wrapper carries Concord's body.
    /// </summary>
    public const string HostOwned = "[Concord.Coex] host-owned";

    /// <summary>
    ///     The host's instruction stream could not be converted safely and was left unchanged.
    /// </summary>
    public const string StreamRejected = "[Concord.Coex] stream-rejected";

    /// <summary>
    ///     The notifier hook installed, so late contention is recoverable rather than merely reported.
    /// </summary>
    public const string HookInstalled = "[Concord.Coex] hook-installed";

    /// <summary>
    ///     The notifier hook, or one live Harmony copy, was refused whole, so a patch made through
    ///     that copy stays unseen.
    /// </summary>
    public const string HookUnavailable = "[Concord.Coex] hook-unavailable";

    /// <summary>
    ///     One live Harmony copy armed with a feature off, because its shared patch state cannot be read.
    /// </summary>
    public const string HookDegraded = "[Concord.Coex] hook-degraded";

    /// <summary>
    ///     A Harmony copy is loaded reflection-only, so nothing can run through it and it is left alone.
    /// </summary>
    public const string HookSkipped = "[Concord.Coex] hook-skipped";

    /// <summary>
    ///     One injection's local selector stopped resolving because another mod changed the composed
    ///     body, so Concord dropped that injection and kept every other one on the target.
    /// </summary>
    public const string InjectionEvicted = "[Concord.Coex] injection-evicted";

    /// <summary>
    ///     A loaded Harmony copy refused to rebuild a method it has a patch on, so the Concord change
    ///     that asked for the rebuild failed with that reason. Harmony's standing wrapper is unchanged.
    /// </summary>
    public const string RebuildRefused = "[Concord.Coex] rebuild-refused";

    /// <summary>
    ///     Harmony's wrapper build now reads Concord's composed body on this Harmony copy.
    /// </summary>
    public const string CopierHookInstalled = "[Concord.Coex] copier-hook-installed";

    /// <summary>
    ///     Handing Concord's composed body to a Harmony copier that was already building threw, so that
    ///     one wrapper build runs without Concord's injections inside it.
    /// </summary>
    public const string CopierHookUnavailable = "[Concord.Coex] copier-hook-unavailable";
}
