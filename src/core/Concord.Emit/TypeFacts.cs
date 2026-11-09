using System.Reflection.Metadata;

namespace Concord.Emit;

internal static class TypeFacts {
    internal static bool IsSzArray(Type type) {
#if NET
        return type.IsSZArray;
#else
        return type.IsArray && type.GetArrayRank() == 1 && type == type.GetElementType()!.MakeArrayType();
#endif
    }

    internal static bool IsGenericMethodParameter(Type type) {
#if NET
        return type.IsGenericMethodParameter;
#else
        return type.IsGenericParameter && type.DeclaringMethod is not null;
#endif
    }

    internal static bool IsGenericTypeParameter(Type type) {
#if NET
        return type.IsGenericTypeParameter;
#else
        return type.IsGenericParameter && type.DeclaringMethod is null;
#endif
    }

    internal static bool IsValueType(Type type) {
#if NET
        return type.IsValueType;
#else
        if (IsFunctionPointer(type) || type.IsByRef || type.IsPointer || type.IsArray || type.IsGenericParameter || type == typeof(Enum)) {
            return false;
        }

        Type? baseType = type.BaseType;
        return baseType == typeof(ValueType) || baseType == typeof(Enum);
#endif
    }

    internal static bool IsFunctionPointer(Type type) {
#if NET
        return type.IsFunctionPointer;
#else
        return type.FullName == "System.MonoFNPtrFakeClass";
#endif
    }

    internal static bool IsUnmanagedFunctionPointer(Type type) {
#if NET
        return type.IsUnmanagedFunctionPointer;
#else
        return false;
#endif
    }

    internal static Type FunctionPointerReturnType(Type type) {
#if NET
        return type.GetFunctionPointerReturnType();
#else
        throw Unreflectable(type);
#endif
    }

    internal static Type[] FunctionPointerParameterTypes(Type type) {
#if NET
        return type.GetFunctionPointerParameterTypes();
#else
        throw Unreflectable(type);
#endif
    }

    internal static Type[] FunctionPointerCallingConventions(Type type) {
#if NET
        return type.GetFunctionPointerCallingConventions();
#else
        throw Unreflectable(type);
#endif
    }

    internal static bool IsByRefLike(Type type) {
#if NET
        return type.IsByRefLike;
#else
        if (IsFunctionPointer(type)) {
            return false;
        }

        foreach (System.Reflection.CustomAttributeData attribute in type.GetCustomAttributesData()) {
            if (attribute.AttributeType.FullName == "System.Runtime.CompilerServices.IsByRefLikeAttribute") {
                return true;
            }
        }

        return false;
#endif
    }

#if !NET
    private static NotSupportedException Unreflectable(Type type) {
        return new NotSupportedException(
            $"Concord cannot read the shape of the function pointer '{type}' on this runtime. "
            + "Type.GetFunctionPointerReturnType and its siblings need net8 or later, and reflecting a "
            + "function-pointer type on Mono 6.12 overflows the process stack. Concord refuses the method instead.");
    }
#endif
}

internal static class Net472Polyfills {
#if !NET
    internal static bool TryAdd<TKey, TValue>(this Dictionary<TKey, TValue> map, TKey key, TValue value) {
        if (map.ContainsKey(key)) {
            return false;
        }

        map.Add(key, value);
        return true;
    }

    internal static void AddOrUpdate<TKey, TValue>(this System.Runtime.CompilerServices.ConditionalWeakTable<TKey, TValue> table, TKey key, TValue value)
        where TKey : class
        where TValue : class? {
        table.Remove(key);
        table.Add(key, value);
    }
#endif
}

internal static class Guard {
    internal static void ThrowIfNull(object? value, string name) {
        if (value is null) {
            throw new ArgumentNullException(name);
        }
    }
}
