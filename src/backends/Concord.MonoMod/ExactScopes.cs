using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;

namespace Concord.MonoMod;

internal static class ExactScopes {
    internal const string ImporterProperty = "ReflectionImporter";

    internal const string StampedAssembliesField = "CachedAsms";

    internal const string HelperType = "MonoMod.Utils.ReflectionHelper";

    internal const string AssemblyCacheField = "AssemblyCache";

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S3011", Justification = "MonoMod's importer map and assembly cache are private, and binding a scope to its exact assembly needs both.")]
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private static readonly ConditionalWeakTable<Assembly, object> Ids = new ConditionalWeakTable<Assembly, object>();

    private static readonly Dictionary<string, int> Combinations = new Dictionary<string, int>(StringComparer.Ordinal);

    private static readonly object IdGate = new object();

    private static readonly object LiveGate = new object();

    private static readonly HashSet<Assembly> Known = new HashSet<Assembly>();

    private static readonly Dictionary<string, List<Assembly>> ByName = new Dictionary<string, List<Assembly>>(StringComparer.Ordinal);

    private static readonly Dictionary<int, List<Assembly>> ByHash = new Dictionary<int, List<Assembly>>();

    private static int nextId = int.MinValue;

    private static int knownCount;

    private static HashSet<Assembly> twins = new HashSet<Assembly>();

    private static HashSet<Assembly> colliding = new HashSet<Assembly>();

    internal static int Seed(ModuleDefinition module) {
        if (StampedOf(module, ImporterProperty, StampedAssembliesField) is not { } stamped) {
            return -1;
        }

        Assembly[] live = AppDomain.CurrentDomain.GetAssemblies();
        Track(live);

        int seeded = 0;
        foreach (Assembly copy in Volatile.Read(ref colliding)) {
            if (stamped.Contains(copy)) {
                continue;
            }

            AssemblyName name = copy.GetName();
            AssemblyNameReference reference = new AssemblyNameReference(name.Name, name.Version) {
                PublicKeyToken = name.GetPublicKeyToken(),
                Culture = name.CultureInfo?.Name ?? string.Empty,
                HashAlgorithm = (AssemblyHashAlgorithm)uint.MaxValue,
                Hash = Stamp(IdOf(copy, live)),
            };

            module.AssemblyReferences.Add(reference);
            stamped[copy] = reference;
            seeded++;
        }

        return seeded;
    }

    internal static int Bind(ModuleDefinition module) {
        return Bind(module, out _);
    }

    internal static int Bind(ModuleDefinition module, out int shared) {
        return Bind(module, ImporterProperty, StampedAssembliesField, HelperType, AssemblyCacheField, out shared);
    }

    internal static int Bind(ModuleDefinition module, string importerProperty, string stampedField, string helperType, string cacheField, out int shared) {
        shared = 0;
        if (StampedOf(module, importerProperty, stampedField) is not { } stamped ||
            typeof(MMReflectionImporter).Assembly.GetType(helperType)?.GetField(cacheField, Hidden)?.GetValue(null) is not IDictionary cache) {
            return -1;
        }

        Dictionary<AssemblyNameReference, Assembly?> owners = new Dictionary<AssemblyNameReference, Assembly?>();
        foreach (DictionaryEntry entry in stamped) {
            if (entry.Key is Assembly assembly && entry.Value is AssemblyNameReference reference && IsStamped(reference)) {
                owners[reference] = owners.ContainsKey(reference) ? null : assembly;
            }
        }

        Assembly[] live = AppDomain.CurrentDomain.GetAssemblies();
        Track(live);

        int bound = 0;
        bool namesATwin = false;
        HashSet<Assembly> twinCopies = Volatile.Read(ref twins);
        foreach (KeyValuePair<AssemblyNameReference, Assembly?> owner in owners) {
            if (owner.Value is null) {
                shared++;
                continue;
            }

            if (Ids.TryGetValue(owner.Value, out object? id) || SharesStamp(owner.Value, live)) {
                Restamp(owner.Key, (int?)id ?? IdOf(owner.Value, live));
            }

            string key = owner.Key.GetRuntimeHashedFullName();
            lock (cache) {
                cache[key] = new WeakReference(owner.Value);
            }

            namesATwin |= twinCopies.Contains(owner.Value);
            bound++;
        }

        if (namesATwin) {
            new Carriers(module, owners, twinCopies, cache, live).Visit();
        }

        return bound;
    }

    private static IDictionary? StampedOf(ModuleDefinition module, string importerProperty, string stampedField) {
        return typeof(ModuleDefinition).GetProperty(importerProperty, Hidden)?.GetValue(module) is { } importer
            ? importer.GetType().GetField(stampedField, Hidden)?.GetValue(importer) as IDictionary
            : null;
    }

    private static bool IsStamped(AssemblyNameReference reference) {
        byte[]? hash = reference.Hash;
        byte[] prefix = ReflectionHelper.AssemblyHashPrefix;
        if (hash is null || hash.Length != prefix.Length + 4) {
            return false;
        }

        for (int index = 0; index < prefix.Length; index++) {
            if (hash[index] != prefix[index]) {
                return false;
            }
        }

        return true;
    }

    private static bool SharesStamp(Assembly assembly, Assembly[] live) {
        int hash = assembly.GetHashCode();
        foreach (Assembly other in live) {
            if (ReferenceEquals(other, assembly)) {
                continue;
            }

            bool sameStamp = other.GetHashCode() == hash || (Ids.TryGetValue(other, out object? id) && (int)id == hash);
            if (sameStamp && other.FullName == assembly.FullName) {
                return true;
            }
        }

        return false;
    }

    private static void Track(Assembly[] live) {
        if (Volatile.Read(ref knownCount) == live.Length) {
            return;
        }

        lock (LiveGate) {
            HashSet<Assembly>? nextTwins = null;
            HashSet<Assembly>? nextColliding = null;
            foreach (Assembly loaded in live) {
                if (!Known.Add(loaded)) {
                    continue;
                }

                string name = loaded.GetName().Name;
                if (!ByName.TryGetValue(name, out List<Assembly>? sameName)) {
                    ByName[name] = sameName = new List<Assembly>();
                }

                sameName.Add(loaded);
                if (sameName.Count > 1) {
                    nextTwins ??= new HashSet<Assembly>(twins);
                    nextTwins.UnionWith(sameName);
                }

                int hash = loaded.GetHashCode();
                if (!ByHash.TryGetValue(hash, out List<Assembly>? sameHash)) {
                    ByHash[hash] = sameHash = new List<Assembly>();
                }

                foreach (Assembly other in sameHash) {
                    if (other.FullName == loaded.FullName) {
                        nextColliding ??= new HashSet<Assembly>(colliding);
                        nextColliding.Add(other);
                        nextColliding.Add(loaded);
                    }
                }

                sameHash.Add(loaded);
            }

            if (nextTwins is not null) {
                Volatile.Write(ref twins, nextTwins);
            }

            if (nextColliding is not null) {
                Volatile.Write(ref colliding, nextColliding);
            }

            Volatile.Write(ref knownCount, live.Length);
        }
    }

    private static int IdOf(Assembly assembly, Assembly[] live) {
        lock (IdGate) {
            if (Ids.TryGetValue(assembly, out object? assigned)) {
                return (int)assigned;
            }

            int id = Fresh(live);
            Ids.Add(assembly, id);
            return id;
        }
    }

    private static int CombinationOf(List<AssemblyNameReference> copies, Assembly[] live) {
        List<string> names = new List<string>(copies.Count);
        foreach (AssemblyNameReference copy in copies) {
            names.Add(copy.GetRuntimeHashedFullName());
        }

        names.Sort(StringComparer.Ordinal);
        string key = string.Join("|", names.ToArray());
        lock (IdGate) {
            if (!Combinations.TryGetValue(key, out int id)) {
                Combinations[key] = id = Fresh(live);
            }

            return id;
        }
    }

    private static int Fresh(Assembly[] live) {
        HashSet<int> taken = new HashSet<int>();
        foreach (Assembly other in live) {
            taken.Add(other.GetHashCode());
        }

        int id = nextId++;
        while (taken.Contains(id)) {
            id = nextId++;
        }

        return id;
    }

    private static byte[] Stamp(int id) {
        byte[] prefix = ReflectionHelper.AssemblyHashPrefix;
        byte[] hash = new byte[prefix.Length + 4];
        Array.Copy(prefix, hash, prefix.Length);
        Array.Copy(BitConverter.GetBytes(id), 0, hash, prefix.Length, 4);
        return hash;
    }

    private static void Restamp(AssemblyNameReference reference, int id) {
        byte[] hash = (byte[])reference.Hash.Clone();
        Array.Copy(BitConverter.GetBytes(id), 0, hash, ReflectionHelper.AssemblyHashPrefix.Length, 4);
        reference.Hash = hash;
    }

    private static List<AssemblyNameReference>? Union(List<AssemblyNameReference>? into, List<AssemblyNameReference>? from) {
        if (from is null) {
            return into;
        }

        into ??= new List<AssemblyNameReference>();
        foreach (AssemblyNameReference copy in from) {
            if (!into.Contains(copy)) {
                into.Add(copy);
            }
        }

        return into;
    }

    private sealed class Carriers {
        private readonly ModuleDefinition module;

        private readonly Dictionary<AssemblyNameReference, Assembly?> owners;

        private readonly HashSet<Assembly> twinCopies;

        private readonly IDictionary cache;

        private readonly Assembly[] live;

        private readonly Dictionary<AssemblyNameReference, Assembly> carried = new Dictionary<AssemblyNameReference, Assembly>();

        internal Carriers(ModuleDefinition module, Dictionary<AssemblyNameReference, Assembly?> owners, HashSet<Assembly> twinCopies, IDictionary cache, Assembly[] live) {
            this.module = module;
            this.owners = owners;
            this.twinCopies = twinCopies;
            this.cache = cache;
            this.live = live;
        }

        internal void Visit() {
            foreach (TypeDefinition type in module.Types) {
                foreach (MethodDefinition method in type.Methods) {
                    Visit(method);
                }
            }
        }

        private void Visit(MethodDefinition method) {
            Copies(method.ReturnType);
            foreach (ParameterDefinition parameter in method.Parameters) {
                Copies(parameter.ParameterType);
            }

            if (!method.HasBody) {
                return;
            }

            foreach (VariableDefinition variable in method.Body.Variables) {
                Copies(variable.VariableType);
            }

            foreach (ExceptionHandler handler in method.Body.ExceptionHandlers) {
                if (handler.CatchType is not null) {
                    Copies(handler.CatchType);
                }
            }

            foreach (Instruction instruction in method.Body.Instructions) {
                switch (instruction.Operand) {
                    case TypeReference type:
                        Copies(type);
                        break;
                    case MethodReference called:
                        Copies(called);
                        break;
                    case FieldReference field:
                        Copies(field.DeclaringType);
                        break;
                    case Mono.Cecil.CallSite site:
                        Copies(site.ReturnType);
                        foreach (ParameterDefinition parameter in site.Parameters) {
                            Copies(parameter.ParameterType);
                        }

                        break;
                }
            }
        }

        private void Copies(MethodReference method) {
            if (method is not GenericInstanceMethod instance) {
                Copies(method.DeclaringType);
                return;
            }

            List<AssemblyNameReference>? named = Copies(instance.ElementMethod.DeclaringType);
            List<AssemblyNameReference>? arguments = null;
            foreach (TypeReference argument in instance.GenericArguments) {
                arguments = Union(arguments, Copies(argument));
            }

            if (arguments is not null) {
                Carry(instance.ElementMethod.DeclaringType, Union(arguments, named)!);
            }
        }

        private List<AssemblyNameReference>? Copies(TypeReference? type) {
            switch (type) {
                case null:
                case GenericParameter:
                    return null;
                case GenericInstanceType instance:
                    List<AssemblyNameReference>? named = Own(instance.ElementType);
                    List<AssemblyNameReference>? arguments = null;
                    foreach (TypeReference argument in instance.GenericArguments) {
                        arguments = Union(arguments, Copies(argument));
                    }

                    if (arguments is not null) {
                        Carry(instance, arguments);
                    }

                    return Union(arguments, named);
                case TypeSpecification specification:
                    return Copies(specification.ElementType);
                default:
                    return Own(type);
            }
        }

        private List<AssemblyNameReference>? Own(TypeReference type) {
            return type.Scope is AssemblyNameReference scope && owners.TryGetValue(scope, out Assembly? owner) && owner is not null && twinCopies.Contains(owner)
                ? new List<AssemblyNameReference> { scope }
                : null;
        }

        private void Carry(TypeReference carrier, List<AssemblyNameReference> copies) {
            TypeReference root = carrier is GenericInstanceType instance ? instance.ElementType : carrier;
            while (root.DeclaringType is not null) {
                root = root.DeclaringType;
            }

            if (root.Scope is not AssemblyNameReference template ||
                !(carried.TryGetValue(template, out Assembly? owner) || (owners.TryGetValue(template, out owner) && owner is not null))) {
                return;
            }

            AssemblyNameReference scope = Scoped(template, CombinationOf(copies, live));
            lock (cache) {
                cache[scope.GetRuntimeHashedFullName()] = new WeakReference(owner);
            }

            carried[scope] = owner;
            root.Scope = scope;
        }

        private AssemblyNameReference Scoped(AssemblyNameReference template, int id) {
            foreach (AssemblyNameReference existing in module.AssemblyReferences) {
                if (existing.Name == template.Name && existing.Version == template.Version && IsStamped(existing) &&
                    BitConverter.ToInt32(existing.Hash, ReflectionHelper.AssemblyHashPrefix.Length) == id) {
                    return existing;
                }
            }

            AssemblyNameReference scope = new AssemblyNameReference(template.Name, template.Version) {
                PublicKeyToken = template.PublicKeyToken,
                Culture = template.Culture,
                HashAlgorithm = template.HashAlgorithm,
                Hash = Stamp(id),
            };

            module.AssemblyReferences.Add(scope);
            return scope;
        }
    }
}
