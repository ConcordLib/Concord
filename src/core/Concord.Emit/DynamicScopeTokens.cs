using System.Reflection;
using System.Reflection.Emit;

namespace Concord.Emit;

internal sealed class DynamicScopeTokens : ITokenSource {
    private readonly DynamicILInfo scope;

    internal DynamicScopeTokens(DynamicILInfo scope) {
        this.scope = scope;
    }

    public int TokenFor(Type type) {
        return scope.GetTokenFor(type.TypeHandle);
    }

    public int TokenFor(FieldInfo field) {
        return Owner(field.DeclaringType) is { } owner
            ? scope.GetTokenFor(field.FieldHandle, owner.TypeHandle)
            : scope.GetTokenFor(field.FieldHandle);
    }

    public int TokenFor(MethodBase method) {
        if (method is DynamicMethod dynamic) {
            return scope.GetTokenFor(dynamic);
        }

        return Owner(method.DeclaringType) is { } owner
            ? scope.GetTokenFor(method.MethodHandle, owner.TypeHandle)
            : scope.GetTokenFor(method.MethodHandle);
    }

    public int TokenFor(string literal) {
        return scope.GetTokenFor(literal);
    }

    public int TokenForLocals(byte[] signature) {
        return scope.GetTokenFor(signature);
    }

    private static Type? Owner(Type? declaring) {
        return declaring is { IsGenericType: true } or { IsArray: true } ? declaring : null;
    }
}
