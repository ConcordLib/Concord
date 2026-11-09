using System.Reflection;
using Concord.Emit;

namespace Concord.Backend;

/// <summary>
///     Puts a composed body on a live method for Concord, and takes it back off.
/// </summary>
public interface IPatchBackend {
    /// <summary>
    ///     The name this backend reports in a log line, matching its package id.
    /// </summary>
    string Name { get; }

    /// <summary>
    ///     How many methods this backend currently has a body on.
    /// </summary>
    int PatchedTargetCount { get; }

    /// <summary>
    ///     Decides up front whether this backend can carry a patch on <paramref name="target" /> at all.
    /// </summary>
    /// <param name="target">The method the caller wants to patch.</param>
    /// <returns>An accepting verdict, or a refusal carrying the code and message to report.</returns>
    TargetVerdict Judge(MethodBase target);

    /// <summary>
    ///     Installs or replaces the one composed body for <paramref name="target" />, built from every
    ///     live injection on it plus <paramref name="added" />.
    /// </summary>
    /// <param name="target">The method to patch.</param>
    /// <param name="added">The injections this call contributes to the target's composed body.</param>
    /// <returns>A handle that reports patch state and removes only the injections it owns when disposed.</returns>
    IBackendHandle ApplyComposed(MethodBase target, IReadOnlyList<Injection> added);

    /// <summary>
    ///     Lists every patch owner this backend has live on <paramref name="target" />, in run order.
    /// </summary>
    /// <param name="target">The method to inspect.</param>
    /// <returns>Distinct owner ids in run order, empty when this backend has nothing on the method.</returns>
    IReadOnlyList<string> OwnersOf(MethodBase target);

    /// <summary>
    ///     Reports whether this backend's own publish mechanism currently owns <paramref name="target" />'s body.
    /// </summary>
    /// <param name="target">The method to check.</param>
    /// <returns><see langword="true" /> if this backend published the body currently in effect for <paramref name="target" />.</returns>
    bool OwnsBody(MethodBase target);

    /// <summary>
    ///     Recomposes <paramref name="target" />'s body from its live injections, adding and removing nothing.
    /// </summary>
    /// <param name="target">The method to rebuild.</param>
    /// <returns>
    ///     <see langword="true" /> if it recomposed <paramref name="target" />,
    ///     <see langword="false" /> for no live injections, a foreign owner, a logged failure left on the original body, or a body this backend wrote in place.
    /// </returns>
    bool Recompose(MethodBase target);
}
