using System.Reflection;
using System.Runtime.CompilerServices;
using WaylithClr;

namespace Concord.Waylith;

internal static class ForkApi {
    private const int Scratch = 64;

    private const BindingFlags AnyMember =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static bool Supported() {
        return MethodBodyUpdater.IsSupported;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Type Pipeline() {
        return typeof(MethodBodyTransformer);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static RuntimeMethodHandle DefinitionHandle(MethodBase method) {
        return MethodBodyUpdater.GetDefinitionHandle(method);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string? MissingStageMember() {
        (Type Owner, string Member)[] needed = [
            (typeof(MethodBodyUpdater), nameof(MethodBodyUpdater.GetDefinitionHandle)),
            (typeof(MethodBodyUpdater), nameof(MethodBodyUpdater.TryGetCurrentMethodBody)),
            (typeof(MethodBodyUpdater), nameof(MethodBodyUpdater.TryGetDefaultMethodBody)),
            (typeof(MethodBodyUpdater), nameof(MethodBodyUpdater.GetTokenFor)),
            (typeof(MethodBodyUpdater), nameof(MethodBodyUpdater.GetTokenForSignature)),
            (typeof(MethodBodyUpdater), nameof(MethodBodyUpdater.IsSupported)),
            (typeof(MethodBodyTransformer), nameof(MethodBodyTransformer.Register)),
            (typeof(MethodBodyTransformer), nameof(MethodBodyTransformer.Rebuild)),
            (typeof(MethodBodyBuilder), nameof(MethodBodyBuilder.Write)),
            (typeof(MethodBodyBuilder), nameof(MethodBodyBuilder.GetTokenForSignature)),
            (typeof(MethodBodyBuilder), nameof(MethodBodyBuilder.GetTokenFor)),
            (typeof(MethodBodyBuilder), nameof(MethodBodyBuilder.AddOffsetMapEntries)),
        ];

        foreach ((Type owner, string member) in needed) {
            if (owner.GetMember(member, AnyMember).Length == 0) {
                return owner.FullName + "." + member;
            }
        }

        return null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int TokenFor(Module target, MethodBase method) {
        return MethodBodyUpdater.GetTokenFor(target, method);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int TokenFor(Module target, FieldInfo field) {
        return MethodBodyUpdater.GetTokenFor(target, field);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int TokenFor(Module target, Type type) {
        return MethodBodyUpdater.GetTokenFor(target, type);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int TokenFor(Module target, string literal) {
        return MethodBodyUpdater.GetTokenFor(target, literal);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int TokenForSignature(Module target, byte[] signature) {
        return MethodBodyUpdater.GetTokenForSignature(target, signature);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static HostBody? ReadDefault(MethodBase target) {
        return Read(target, false);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static HostBody? ReadCurrent(MethodBase target) {
        return Read(target, true);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static HostBody? Copy(in MethodBodyView body, ReadOnlySpan<MethodBodyClause> clauses) {
        int count = body.ClauseCount;
        if (count < 0 || count > clauses.Length) {
            return null;
        }

        HostClause[] copied = new HostClause[count];
        for (int i = 0; i < count; i++) {
            MethodBodyClause clause = clauses[i];
            copied[i] = new HostClause(
                (int)clause.Flags,
                clause.TryOffset,
                clause.TryLength,
                clause.HandlerOffset,
                clause.HandlerLength,
                clause.ClassTokenOrFilterOffset);
        }

        return new HostBody(
            body.IL.ToArray(),
            body.LocalSignature.ToArray(),
            body.LocalSignatureToken,
            body.MaxStackSize,
            body.InitLocals,
            copied);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static HostBody? Read(MethodBase target, bool current) {
        MethodBodyClause[] clauses = new MethodBodyClause[Scratch];
        MethodBodyView body;
        bool read = current
            ? MethodBodyUpdater.TryGetCurrentMethodBody(target, clauses, out body)
            : MethodBodyUpdater.TryGetDefaultMethodBody(target, clauses, out body);

        if (!read) {
            return null;
        }

        if (body.ClauseCount > clauses.Length) {
            clauses = new MethodBodyClause[body.ClauseCount];
            read = current
                ? MethodBodyUpdater.TryGetCurrentMethodBody(target, clauses, out body)
                : MethodBodyUpdater.TryGetDefaultMethodBody(target, clauses, out body);

            if (!read) {
                return null;
            }
        }

        return Copy(body, clauses);
    }
}
