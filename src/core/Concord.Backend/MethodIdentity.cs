using System.Reflection;
using Concord.Emit;

namespace Concord.Backend;

/// <summary>
///     Resolves a method to one canonical <see cref="MethodBase" /> so the same target reached through
///     different reflection paths compares and hashes as a single key.
/// </summary>
public static class MethodIdentity {
    /// <summary>
    ///     Returns the canonical <see cref="MethodBase" /> for a target, or the target itself when it has no
    ///     canonical form (open generics, module-level methods, runtimes without handle resolution).
    /// </summary>
    /// <param name="target">The method to normalize.</param>
    /// <returns>The canonical instance to use as a routing key.</returns>
    public static MethodBase Normalize(MethodBase target) {
        if (target.DeclaringType == null || target.DeclaringType.IsGenericTypeDefinition || target.IsGenericMethodDefinition) {
            return target;
        }

        try {
            return MethodBase.GetMethodFromHandle(target.MethodHandle, target.DeclaringType.TypeHandle) ?? target;
        } catch (NotSupportedException) {
            return target;
        }
    }

    /// <summary>
    ///     Returns the routing key a target shares with every other instantiation the runtime compiles it
    ///     into one body with. Reference-type generic arguments all collapse to <see cref="object" />, so
    ///     <c>Box&lt;string&gt;.Ping</c> and <c>Box&lt;Version&gt;.Ping</c> land on one key while
    ///     <c>Box&lt;int&gt;.Ping</c>, which the runtime gives its own body, keeps its own.
    /// </summary>
    /// <param name="target">The method to key.</param>
    /// <returns>The canonical shared-body key, or <see cref="Normalize" /> when the body is not shared.</returns>
    public static MethodBase SharedBodyKey(MethodBase target) {
        if (!WrapperComposer.SharesGenericBody(target)) {
            return Normalize(target);
        }

        Type declaringType = target.DeclaringType!;
        Type[] arguments = declaringType.GetGenericArguments();
        Type[] canonical = new Type[arguments.Length];
        for (int i = 0; i < arguments.Length; i++) {
            canonical[i] = arguments[i].IsValueType || arguments[i].IsGenericParameter ? arguments[i] : typeof(object);
        }

        try {
            Type canonicalType = declaringType.GetGenericTypeDefinition().MakeGenericType(canonical);
            return MethodBase.GetMethodFromHandle(target.MethodHandle, canonicalType.TypeHandle) ?? Normalize(target);
        } catch (Exception) {
            return Normalize(target);
        }
    }
}
