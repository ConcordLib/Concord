using System.Reflection;
using System.Runtime.InteropServices;

namespace Concord.Waylith;

/// <summary>
///     Mints the metadata tokens a composed body's IL carries as operands. A token is only valid in the
///     module it was minted against, so the module is passed on every call.
/// </summary>
public interface IMetadataMinter {
    /// <summary>Mints a token for <paramref name="method" /> against <paramref name="target" />.</summary>
    /// <param name="target">The module whose body will carry the token.</param>
    /// <param name="method">The method the body refers to.</param>
    /// <returns>A metadata token, meaningless in any other module.</returns>
    int Token(Module target, MethodBase method);

    /// <summary>Mints a token for <paramref name="field" /> against <paramref name="target" />.</summary>
    /// <param name="target">The module whose body will carry the token.</param>
    /// <param name="field">The field the body refers to.</param>
    /// <returns>A metadata token, meaningless in any other module.</returns>
    int Token(Module target, FieldInfo field);

    /// <summary>Mints a token for <paramref name="type" /> against <paramref name="target" />.</summary>
    /// <param name="target">The module whose body will carry the token.</param>
    /// <param name="type">The type the body refers to.</param>
    /// <returns>A metadata token, meaningless in any other module.</returns>
    int Token(Module target, Type type);

    /// <summary>Interns <paramref name="literal" /> in <paramref name="target" /> and hands back its token.</summary>
    /// <param name="target">The module whose body will carry the token.</param>
    /// <param name="literal">The string the body loads.</param>
    /// <returns>A metadata token, meaningless in any other module.</returns>
    int Token(Module target, string literal);

    /// <summary>Mints a token for <paramref name="signature" /> against <paramref name="target" />.</summary>
    /// <param name="target">The module whose body will carry the token.</param>
    /// <param name="signature">An encoded ECMA-335 LocalVarSig blob, or the method signature a <c>calli</c> call site names.</param>
    /// <returns>A <c>StandAloneSig</c> token a body header or a <c>calli</c> operand can point at.</returns>
    int LocalsToken(Module target, byte[] signature);
}

/// <summary>
///     Mints tokens through the forked runtime's own token api. Check <see cref="IsAvailable" /> first: on a
///     stock runtime the api is not there and every call here throws.
/// </summary>
public sealed class ForkMinter : IMetadataMinter {
    private const string UpdaterName = RuntimeCapability.UpdaterTypeName;

    private const string TokenEntry = "GetTokenFor";

    private const string LocalsEntry = "GetTokenForSignature";

    /// <summary>
    ///     <see langword="true" /> only when this runtime ships the token api. It is <see langword="false" />
    ///     on any runtime but the fork, and a caller that mints anyway gets an exception per call.
    /// </summary>
    public static bool IsAvailable { get; } = Type.GetType(UpdaterName, throwOnError: false) is not null;

    /// <inheritdoc />
    /// <remarks>A generic method operand needs <see cref="RuntimeCapability.MintsMethodSpecs" /> checked first; the MethodSpec the runtime builds here is unusable without it.</remarks>
    /// <exception cref="PlatformNotSupportedException">This runtime has no token api, so Concord installs no backend.</exception>
    public int Token(Module target, MethodBase method) {
        Require(TokenEntry, nameof(MethodBase));
        return ForkApi.TokenFor(target, method);
    }

    /// <inheritdoc />
    /// <exception cref="PlatformNotSupportedException">This runtime has no token api, so Concord installs no backend.</exception>
    public int Token(Module target, FieldInfo field) {
        Require(TokenEntry, nameof(FieldInfo));
        return ForkApi.TokenFor(target, field);
    }

    /// <inheritdoc />
    /// <exception cref="PlatformNotSupportedException">This runtime has no token api, so Concord installs no backend.</exception>
    public int Token(Module target, Type type) {
        Require(TokenEntry, nameof(Type));
        return ForkApi.TokenFor(target, type);
    }

    /// <inheritdoc />
    /// <exception cref="PlatformNotSupportedException">This runtime has no token api, so Concord installs no backend.</exception>
    public int Token(Module target, string literal) {
        Require(TokenEntry, nameof(String));
        return ForkApi.TokenFor(target, literal);
    }

    /// <inheritdoc />
    /// <exception cref="PlatformNotSupportedException">This runtime has no token api, so Concord installs no backend.</exception>
    public int LocalsToken(Module target, byte[] signature) {
        Require(LocalsEntry, "ReadOnlySpan<byte>");
        return ForkApi.TokenForSignature(target, signature);
    }

    private static void Require(string entry, string operandKind) {
        if (IsAvailable) {
            return;
        }

        throw new PlatformNotSupportedException(
            $"'{UpdaterName}'.{entry}(Module, {operandKind}) is not present on this runtime " +
            $"('{RuntimeInformation.FrameworkDescription}'). Minting metadata tokens needs the WaylithCLR fork of " +
            "CoreLib, so Concord installs no backend here and nothing can be patched.");
    }
}
