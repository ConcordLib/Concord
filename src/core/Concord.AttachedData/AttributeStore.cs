using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Concord.AttachedData;

internal static class AttributeStore {
    private const string ManifestTypeName = "Concord.Generated.PatchManifest";

    private static readonly ConditionalWeakTable<Assembly, Lazy<Table>> Tables = new ConditionalWeakTable<Assembly, Lazy<Table>>();

    internal static IEnumerable<T> GetAll<T>(MemberInfo member)
        where T : Attribute {
        List<Attribute>? stored = StoredOn(member);
        if (stored is null) {
            return member.GetCustomAttributes<T>();
        }

        List<T> found = new List<T>();
        foreach (Attribute attribute in stored) {
            if (attribute is T match) {
                found.Add(match);
            }
        }

        return found;
    }

    internal static T? Get<T>(MemberInfo member)
        where T : Attribute {
        List<Attribute>? stored = StoredOn(member);
        return stored is null ? member.GetCustomAttribute<T>() : First<T>(stored);
    }

    internal static T? Get<T>(ParameterInfo parameter)
        where T : Attribute {
        MemberInfo member = parameter.Member;
        int token = TokenOf(member);
        if (token != 0) {
            Module module = member.Module;
            if (TableOf(module.Assembly).Parameters.TryGetValue((module, token, parameter.Position), out List<Attribute>? stored)) {
                return First<T>(stored);
            }
        }

        return parameter.GetCustomAttribute<T>();
    }

    internal static T? Get<T>(Assembly assembly)
        where T : Attribute {
        List<Attribute>? stored = TableOf(assembly).Assembly;
        return stored is null ? assembly.GetCustomAttribute<T>() : First<T>(stored);
    }

    internal static bool TryGetDescribedTypes<T>(Assembly assembly, out IReadOnlyList<Type> types)
        where T : Attribute {
        Table table = TableOf(assembly);
        if (!table.HasManifest) {
            types = Array.Empty<Type>();
            return false;
        }

        List<Type> found = new List<Type>();
        foreach (Type type in table.Types) {
            (Module, int) key = (type.Module, type.MetadataToken);
            if (table.Unreadable.ContainsKey(key) || First<T>(table.Members[key]) is not null) {
                found.Add(type);
            }
        }

        types = found;
        return true;
    }

    private static List<Attribute>? StoredOn(MemberInfo member) {
        int token = TokenOf(member);
        if (token == 0) {
            return null;
        }

        Module module = member.Module;
        Table table = TableOf(module.Assembly);
        if (table.Unreadable.TryGetValue((module, token), out Exception? error)) {
            throw error;
        }

        return table.Members.TryGetValue((module, token), out List<Attribute>? stored) ? stored : null;
    }

    private static T? First<T>(List<Attribute> stored)
        where T : Attribute {
        foreach (Attribute attribute in stored) {
            if (attribute is T match) {
                return match;
            }
        }

        return null;
    }

    private static int TokenOf(MemberInfo member) {
        try {
            return member.MetadataToken;
        } catch (InvalidOperationException) {
            return 0;
        }
    }

    private static Table TableOf(Assembly assembly) {
        if (!Tables.TryGetValue(assembly, out Lazy<Table>? table)) {
            table = Tables.GetValue(assembly, CreateTable);
        }

        return table.Value;
    }

    private static Lazy<Table> CreateTable(Assembly assembly) {
        return new Lazy<Table>(() => Load(assembly), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private static Table Load(Assembly assembly) {
        string name = "'" + assembly.GetName().Name + "'";
        Recorder? recorder = null;
        try {
            Type? manifest = assembly.GetType(ManifestTypeName, false);
            if (manifest is null) {
                return Table.None;
            }

            MethodInfo describe = manifest.GetMethod("Describe", BindingFlags.Public | BindingFlags.Static, null, [typeof(object)], null)
                                  ?? throw new MissingMethodException(ManifestTypeName, "Describe(object)");
            recorder = new Recorder(assembly);
            describe.Invoke(null, [recorder]);
        } catch (Exception ex) {
            Exception cause = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
            string where = recorder?.Last is { } last ? "after it described " + last : "before it described a member";
            throw new InvalidOperationException(
                "[Concord] the patch manifest of assembly " + name + " failed " + where + ": " + cause.Message, cause);
        }

        if (recorder.Problems.Count > 0) {
            throw new InvalidOperationException(
                "[Concord] the patch manifest of assembly " + name + " describes what the assembly does not declare: " +
                string.Join("; ", recorder.Problems) + ".");
        }

        return recorder.Table;
    }

    private sealed class Table {
        internal static readonly Table None = new Table(false);

        internal Table(bool hasManifest) {
            HasManifest = hasManifest;
        }

        internal bool HasManifest { get; }

        internal List<Attribute>? Assembly { get; set; }

        internal Dictionary<(Module Module, int Token), List<Attribute>> Members { get; } =
            new Dictionary<(Module Module, int Token), List<Attribute>>();

        internal Dictionary<(Module Module, int Token, int Position), List<Attribute>> Parameters { get; } =
            new Dictionary<(Module Module, int Token, int Position), List<Attribute>>();

        internal Dictionary<(Module Module, int Token), Exception> Unreadable { get; } =
            new Dictionary<(Module Module, int Token), Exception>();

        internal List<Type> Types { get; } = new List<Type>();
    }

    private sealed class Recorder : IPatchManifestSink {
        private readonly Assembly assembly;

        internal Recorder(Assembly assembly) {
            this.assembly = assembly;
        }

        internal Table Table { get; } = new Table(true);

        internal List<string> Problems { get; } = new List<string>();

        internal string? Last { get; private set; }

        public void OnAssembly(Attribute attribute) {
            if (attribute is null) {
                Problems.Add("a null attribute on the assembly");
                return;
            }

            (Table.Assembly ??= new List<Attribute>()).Add(attribute);
            Last = "the assembly";
        }

        public void OnMember(string memberId, Attribute attribute) {
            MemberInfo? member = Find(memberId, attribute);
            if (member is null) {
                return;
            }

            (Module, int) key = (member.Module, member.MetadataToken);
            if (!Table.Members.TryGetValue(key, out List<Attribute>? stored)) {
                stored = new List<Attribute>();
                Table.Members.Add(key, stored);
                if (member is Type type && !Table.Unreadable.ContainsKey(key)) {
                    Table.Types.Add(type);
                }
            }

            stored.Add(attribute);
            Last = memberId;
        }

        public void OnParameter(string memberId, int position, Attribute attribute) {
            MemberInfo? member = Find(memberId, attribute);
            if (member is null) {
                return;
            }

            if (member is not MethodBase method || position < 0 || position >= method.GetParameters().Length) {
                Problems.Add(memberId + " (no parameter at position " + position + ")");
                return;
            }

            (Module, int, int) key = (method.Module, method.MetadataToken, position);
            if (!Table.Parameters.TryGetValue(key, out List<Attribute>? stored)) {
                stored = new List<Attribute>();
                Table.Parameters.Add(key, stored);
            }

            stored.Add(attribute);
            Last = memberId + " parameter " + position;
        }

        public void OnUnreadable(string memberId, Exception error) {
            if (error is null) {
                Problems.Add(memberId + " (a null load failure)");
                return;
            }

            MemberInfo? member;
            try {
                member = MemberIds.Resolve(assembly, memberId);
            } catch (Exception ex) {
                Problems.Add(memberId + " (" + ex.GetType().Name + ": " + ex.Message + ")");
                return;
            }

            if (member is not Type type) {
                Problems.Add(memberId + " (no such type)");
                return;
            }

            MarkUnreadable(type, error);
            Last = memberId;
        }

        private MemberInfo? Find(string memberId, Attribute attribute) {
            if (attribute is null) {
                Problems.Add(memberId + " (a null attribute)");
                return null;
            }

            MemberInfo? member;
            try {
                member = MemberIds.Resolve(assembly, memberId);
            } catch (Exception ex) {
                if (!TryMarkOwnerUnreadable(memberId, ex)) {
                    Problems.Add(memberId + " (" + ex.GetType().Name + ": " + ex.Message + ")");
                }

                return null;
            }

            if (member is null) {
                Problems.Add(memberId + " (no such member)");
            }

            return member;
        }

        private bool TryMarkOwnerUnreadable(string memberId, Exception error) {
            if (error is not (TypeLoadException or FileNotFoundException or FileLoadException)) {
                return false;
            }

            Type? owner;
            try {
                owner = MemberIds.ResolveOwner(assembly, memberId);
            } catch (Exception ex) when (ex is TypeLoadException or FileNotFoundException or FileLoadException) {
                return false;
            }

            if (owner is null) {
                return false;
            }

            MarkUnreadable(owner, error);
            return true;
        }

        private void MarkUnreadable(Type type, Exception error) {
            (Module, int) key = (type.Module, type.MetadataToken);
            if (Table.Unreadable.ContainsKey(key)) {
                return;
            }

            Table.Unreadable.Add(key, error);
            if (!Table.Members.ContainsKey(key)) {
                Table.Types.Add(type);
            }
        }
    }
}
