namespace Concord.MonoMod;

/// <summary>
///     Who owns a target method's entry point.
/// </summary>
public enum RouteState {
    /// <summary>
    ///     Concord's own detour owns the entry point, because no foreign patcher has one on it.
    /// </summary>
    Raw,

    /// <summary>
    ///     A foreign patch host owns the entry point, and its wrapper is built over Concord's body. A
    ///     target reaches this only once the host's wrapper build has returned.
    /// </summary>
    HarmonyOwned
}
