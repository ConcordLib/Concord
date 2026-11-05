using System.Reflection;
using Concord.Emit;

namespace Concord.Detour;

/// <summary>
///     Puts a composed body on a live method for Concord, and takes it back off.
/// </summary>
public interface IDetourBackend {
    /// <summary>
    ///     How many methods this backend currently has a body on.
    /// </summary>
    int PatchedTargetCount { get; }

    /// <summary>
    ///     Lists every patch owner this backend has live on <paramref name="target" />, in run order.
    /// </summary>
    /// <param name="target">The method to inspect.</param>
    /// <returns>Distinct owner ids in run order, empty when this backend has nothing on the method.</returns>
    IReadOnlyList<string> OwnersOf(MethodBase target);

    /// <summary>
    ///     Installs or replaces the single detour for <paramref name="target" />, composing one wrapper from
    ///     every currently-live injection on that target plus <paramref name="added" />. Disposing the returned
    ///     handle removes the injections it owns and recomposes (or removes) the target's detour.
    /// </summary>
    /// <param name="target">The method to redirect.</param>
    /// <param name="added">The injections this call contributes to the target's composed wrapper.</param>
    /// <returns>A handle that reports detour state and removes only the injections it owns when disposed.</returns>
    IDetourHandle ApplyComposed(MethodBase target, IReadOnlyList<Injection> added);
}

/// <summary>
///     Lets a caller ask a backend whether it owns a target's body.
/// </summary>
public interface IOwnsBodyQuery {
    /// <summary>
    ///     Reports whether this backend's own publish mechanism currently owns <paramref name="target" />'s body.
    /// </summary>
    /// <param name="target">The method to check.</param>
    /// <returns><see langword="true" /> if this backend published the body currently in effect for <paramref name="target" />.</returns>
    bool OwnsBody(MethodBase target);
}

/// <summary>
///     Lets a caller ask a backend to rebuild a target's composed body from the injections already live
///     on it, adding and removing nothing.
/// </summary>
public interface IRecomposeTarget {
    /// <summary>
    ///     Recomposes <paramref name="target" />'s body from its live injections. Safe to call more than once
    ///     for one target.
    /// </summary>
    /// <param name="target">The method to rebuild.</param>
    /// <returns>
    ///     <see langword="true" /> if it recomposed <paramref name="target" />,
    ///     <see langword="false" /> for no live injections, a foreign owner, a logged failure left on the original body, or a body this backend wrote in place.
    /// </returns>
    bool Recompose(MethodBase target);
}
