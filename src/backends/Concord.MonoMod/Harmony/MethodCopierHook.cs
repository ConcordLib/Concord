using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Concord.Backend;
using Concord.Emit;

namespace Concord.MonoMod.Harmony;

internal static class MethodCopierHook {
    private static readonly object Gate = new object();

    private static readonly Dictionary<Assembly, Binding> Bound = new Dictionary<Assembly, Binding>();

    private static readonly MethodInfo CopyHook = Resolve(nameof(OnCopierBuilt));

    private static readonly MethodInfo ConfigHook = Resolve(nameof(OnCopierBuiltFromConfig));

    internal static string? DeniedMemberForTests { get; set; }

    internal static bool IsHookedOn(Assembly copy) {
        lock (Gate) {
            return copy != null && Bound.ContainsKey(copy);
        }
    }

    internal static IReadOnlyList<string> BoundMembers(Assembly copy) {
        lock (Gate) {
            return copy != null && Bound.TryGetValue(copy, out Binding? binding) ? binding.Members : Array.Empty<string>();
        }
    }

    internal static bool TryInstallFor(Assembly copy, MonoModBackend rawBackend, Action<string> log, out string? missing) {
        missing = null;

        lock (Gate) {
            if (Bound.ContainsKey(copy)) {
                return true;
            }
        }

        Binding? binding = Probe(copy, out missing);
        if (binding == null) {
            return false;
        }

        List<IBackendHandle> landed = new List<IBackendHandle>(binding.Constructors.Count);
        try {
            foreach (ConstructorInfo ctor in binding.Constructors) {
                MethodInfo hook = ctor.GetParameters().Length == 1 ? ConfigHook : CopyHook;
                landed.Add(rawBackend.ApplyComposed(ctor, new[] { new Injection(hook, new InjectAt.Return(0), "concord.copier", 0) }));
            }
        } catch (Exception ex) {
            foreach (IBackendHandle handle in landed) {
                handle.Dispose();
            }

            missing = ExceptionReason.Of(ex);
            return false;
        }

        lock (Gate) {
            if (Bound.ContainsKey(copy)) {
                foreach (IBackendHandle handle in landed) {
                    handle.Dispose();
                }

                return true;
            }

            binding.Handles.AddRange(landed);
            Bound[copy] = binding;
        }

        log(CoexistenceLogMarkers.CopierHookInstalled + " " + Describe(copy) + " through " + string.Join(", ", binding.Members.ToArray()));
        return true;
    }

    internal static void RemoveFor(Assembly copy) {
        Binding? binding;
        lock (Gate) {
            if (!Bound.TryGetValue(copy, out binding)) {
                return;
            }

            Bound.Remove(copy);
        }

        foreach (IBackendHandle handle in binding.Handles) {
            handle.Dispose();
        }
    }

    internal static void ResetForTests() {
        List<Binding> bound;
        lock (Gate) {
            bound = new List<Binding>(Bound.Values);
            Bound.Clear();
        }

        foreach (Binding binding in bound) {
            foreach (IBackendHandle handle in binding.Handles) {
                handle.Dispose();
            }
        }
    }

    private static void OnCopierBuilt(object copier, MethodBase fromMethod, ILGenerator toILGenerator, LocalBuilder[] existingVariables) {
        InsertFeed(copier);
    }

    private static void OnCopierBuiltFromConfig(object copier, object config) {
        InsertFeed(copier);
    }

    private static void InsertFeed(object copier) {
        try {
            Binding? binding = BindingFor(copier.GetType());
            if (binding == null) {
                return;
            }

            if (binding.Reader.GetValue(copier) is not object reader) {
                return;
            }

            if (binding.ReaderMethod.GetValue(reader) is not MethodBase source) {
                return;
            }

            MethodBase key = MethodIdentity.Normalize(source);
            if (!TranspilerParticipant.Registry.HasInjections(key)) {
                return;
            }

            if (binding.Transpilers.GetValue(copier) is not List<MethodInfo> transpilers) {
                return;
            }

            if (transpilers.Contains(TranspilerParticipant.TranspileMethod)) {
                return;
            }

            transpilers.Insert(0, TranspilerParticipant.TranspileMethod);
        } catch (Exception ex) {
            TranspilerParticipant.Log?.Invoke(
                CoexistenceLogMarkers.CopierHookUnavailable + " feeding Concord's body failed: " + ExceptionReason.Of(ex));
        }
    }

    private static Binding? BindingFor(Type copierType) {
        lock (Gate) {
            foreach (KeyValuePair<Assembly, Binding> entry in Bound) {
                if (entry.Value.CopierType == copierType) {
                    return entry.Value;
                }
            }
        }

        return null;
    }

    private static Binding? Probe(Assembly copy, out string? missing) {
        missing = null;

        Type? copier = Denied("MethodCopier") ? null : copy.GetType("HarmonyLib.MethodCopier");
        if (copier == null) {
            missing = "it carries no HarmonyLib.MethodCopier";
            return null;
        }

        FieldInfo? transpilers = Denied("MethodCopier.transpilers")
            ? null
            : copier.GetField("transpilers", BindingFlags.NonPublic | BindingFlags.Instance);
        if (transpilers == null) {
            missing = "HarmonyLib.MethodCopier carries no 'transpilers' field";
            return null;
        }

        FieldInfo? reader = Denied("MethodCopier.reader")
            ? null
            : copier.GetField("reader", BindingFlags.NonPublic | BindingFlags.Instance);
        if (reader == null) {
            missing = "HarmonyLib.MethodCopier carries no 'reader' field";
            return null;
        }

        Type? bodyReader = copy.GetType("HarmonyLib.MethodBodyReader");
        if (bodyReader == null) {
            missing = "it carries no HarmonyLib.MethodBodyReader";
            return null;
        }

        FieldInfo? readerMethod = Denied("MethodBodyReader.method")
            ? null
            : bodyReader.GetField("method", BindingFlags.NonPublic | BindingFlags.Instance);
        if (readerMethod == null) {
            missing = "HarmonyLib.MethodBodyReader carries no 'method' field";
            return null;
        }

        ConstructorInfo? byConfig = null;
        ConstructorInfo? byMethod = null;
        foreach (ConstructorInfo candidate in Denied("MethodCopier..ctor")
                     ? Array.Empty<ConstructorInfo>()
                     : copier.GetConstructors(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)) {
            ParameterInfo[] parameters = candidate.GetParameters();
            if (parameters.Length == 1 && parameters[0].ParameterType.FullName == "HarmonyLib.MethodCreatorConfig") {
                byConfig = candidate;
            } else if (parameters.Length == 3 && parameters[0].ParameterType == typeof(MethodBase) && parameters[1].ParameterType == typeof(ILGenerator)) {
                byMethod = candidate;
            }
        }

        List<ConstructorInfo> constructors = new List<ConstructorInfo>(2);
        List<string> members = new List<string> {
            "MethodCopier.transpilers",
            "MethodCopier.reader",
            "MethodBodyReader.method",
        };

        if (byConfig != null) {
            constructors.Add(byConfig);
            members.Add("MethodCopier..ctor(MethodCreatorConfig)");
        }

        if (byMethod != null) {
            constructors.Add(byMethod);
            members.Add("MethodCopier..ctor(MethodBase, ILGenerator, LocalBuilder[])");
        }

        if (constructors.Count == 0) {
            missing = "HarmonyLib.MethodCopier carries no constructor Concord can hook";
            return null;
        }

        return new Binding(copier, transpilers, reader, readerMethod, constructors, members);
    }

    private static bool Denied(string member) {
        return string.Equals(DeniedMemberForTests, member, StringComparison.Ordinal);
    }

    private static string Describe(Assembly copy) {
        return (copy.GetName().Version?.ToString() ?? "unknown") + " #" + copy.GetHashCode();
    }

    private static MethodInfo Resolve(string name) {
        return typeof(MethodCopierHook).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
               ?? throw new MissingMethodException(nameof(MethodCopierHook), name);
    }

    private sealed class Binding {
        internal Binding(
            Type copierType,
            FieldInfo transpilers,
            FieldInfo reader,
            FieldInfo readerMethod,
            List<ConstructorInfo> constructors,
            List<string> members) {
            CopierType = copierType;
            Transpilers = transpilers;
            Reader = reader;
            ReaderMethod = readerMethod;
            Constructors = constructors;
            Members = members;
            Handles = new List<IBackendHandle>(constructors.Count);
        }

        internal List<IBackendHandle> Handles { get; }

        internal Type CopierType { get; }

        internal FieldInfo Transpilers { get; }

        internal FieldInfo Reader { get; }

        internal FieldInfo ReaderMethod { get; }

        internal List<ConstructorInfo> Constructors { get; }

        internal List<string> Members { get; }
    }
}
