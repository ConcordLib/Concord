using System.Collections;
using System.Reflection;
using MonoMod.Utils;

namespace Concord.MonoMod;

/// <summary>
///     Drops MonoMod's cached reflection lookups that a second copy of an assembly makes ambiguous.
/// </summary>
/// <remarks>
///     A host that swaps a game assembly for a rewritten copy (RimWorld's Prepatcher) marks the old copy
///     reflection-only and loads the new one under the same name. Concord's runtime survives that swap, so
///     MonoMod's static resolve cache still holds members of the old copy. Its cache key for a method on
///     a generic instance type carries no assembly hash, so the next compose gets a stale
///     <see cref="MethodInfo" /> and the runtime rejects the body. Call this once the swap is done.
/// </remarks>
public static class ResolveCache {
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S3011", Justification = "MonoMod's resolve caches are private statics, and clearing them after an assembly swap needs them.")]
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Static;

    /// <summary>
    ///     Removes every cached member and assembly that a reflection-only copy, or a second live copy
    ///     of one assembly name, leaves ambiguous.
    /// </summary>
    /// <returns>The number of entries removed, or -1 when MonoMod's caches could not be found.</returns>
    public static int ForgetReflectionOnly() {
        Type helper = typeof(ReflectionHelper);
        if (helper.GetField("ResolveReflectionCache", Hidden)?.GetValue(null) is not IDictionary members ||
            helper.GetField("AssembliesCache", Hidden)?.GetValue(null) is not IDictionary assemblySets ||
            helper.GetField("AssemblyCache", Hidden)?.GetValue(null) is not IDictionary assemblies) {
            return -1;
        }

        HashSet<string> twins = LoadedTwice();

        int removed = 0;
        removed += Purge(members, value => value is WeakReference r && r.Target is MemberInfo m && IsStale(m, twins));
        removed += Purge(assemblies, value => value is WeakReference r && r.Target is Assembly a && a.ReflectionOnly);
        removed += Purge(assemblySets, value => value is WeakReference[] refs && Array.Exists(refs, r => r.Target is Assembly a && a.ReflectionOnly));
        return removed;
    }

    /// <summary>
    ///     Binds each live assembly to the scope key MonoMod stamps on references imported from it, so
    ///     such a reference resolves back into that assembly and not into another one reporting the same hash.
    /// </summary>
    /// <returns>The number of assemblies bound, or -1 when MonoMod's caches could not be found.</returns>
    public static int BindScopes() {
        if (typeof(ReflectionHelper).GetField("AssemblyCache", Hidden)?.GetValue(null) is not IDictionary assemblies) {
            return -1;
        }

        Dictionary<string, Assembly?> byScope = new Dictionary<string, Assembly?>(StringComparer.Ordinal);
        foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies()) {
            if (loaded.ReflectionOnly) {
                continue;
            }

            string scope = loaded.GetRuntimeHashedFullName();
            byScope[scope] = byScope.ContainsKey(scope) ? null : loaded;
        }

        int bound = 0;
        lock (assemblies) {
            foreach (KeyValuePair<string, Assembly?> entry in byScope) {
                if (entry.Value is null) {
                    continue;
                }

                assemblies[entry.Key] = new WeakReference(entry.Value);
                bound++;
            }
        }

        return bound;
    }

    private static HashSet<string> LoadedTwice() {
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        HashSet<string> twice = new HashSet<string>(StringComparer.Ordinal);
        foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies()) {
            if (!seen.Add(loaded.GetName().Name)) {
                twice.Add(loaded.GetName().Name);
            }
        }

        return twice;
    }

    // A List<Ability>.GetEnumerator lives in mscorlib. Only its generic argument is the stale part.
    private static bool IsStale(MemberInfo member, HashSet<string> twins) {
        if (Ambiguous(member.Module.Assembly, twins)) {
            return true;
        }

        if (member is Type type) {
            return IsStale(type, twins);
        }

        if (member.DeclaringType is not null && IsStale(member.DeclaringType, twins)) {
            return true;
        }

        return member is MethodInfo { IsGenericMethod: true } method
               && Array.Exists(method.GetGenericArguments(), argument => IsStale(argument, twins));
    }

    private static bool IsStale(Type type, HashSet<string> twins) {
        if (Ambiguous(type.Assembly, twins)) {
            return true;
        }

        if (type.HasElementType && type.GetElementType() is Type element) {
            return IsStale(element, twins);
        }

        return type.IsGenericType && !type.IsGenericTypeDefinition
                                  && Array.Exists(type.GetGenericArguments(), argument => IsStale(argument, twins));
    }

    private static bool Ambiguous(Assembly assembly, HashSet<string> twins) {
        return assembly.ReflectionOnly || twins.Contains(assembly.GetName().Name);
    }

    private static int Purge(IDictionary cache, Func<object?, bool> stale) {
        List<object> keys = new List<object>();
        lock (cache) {
            foreach (DictionaryEntry entry in cache) {
                if (stale(entry.Value)) {
                    keys.Add(entry.Key);
                }
            }

            foreach (object key in keys) {
                cache.Remove(key);
            }
        }

        return keys.Count;
    }
}
