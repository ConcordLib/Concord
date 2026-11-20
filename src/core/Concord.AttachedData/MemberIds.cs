using System.Reflection;
using System.Text;

namespace Concord.AttachedData;

internal static class MemberIds {
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S3011:Reflection should not be used to increase accessibility of classes, methods, or fields", Justification = "Concord maps a generated member id back to its member whatever that member's accessibility.")]
    private const BindingFlags Declared = BindingFlags.Public |
                                          BindingFlags.NonPublic |
                                          BindingFlags.Instance |
                                          BindingFlags.Static |
                                          BindingFlags.DeclaredOnly;

    internal static MemberInfo? Resolve(Assembly assembly, string id) {
        Type? type = ResolveOwner(assembly, id);
        if (type is null || id[0] == 'T') {
            return type;
        }

        int open = id.IndexOf('(');
        string head = open < 0 ? id : id.Substring(0, open);
        string name = head.Substring(head.LastIndexOf('.') + 1);
        int arity = name.IndexOf("``", StringComparison.Ordinal);
        string bare = arity < 0 ? name : name.Substring(0, arity);
        foreach (MemberInfo candidate in type.GetMembers(Declared)) {
            if (NameOf(candidate) == bare && Of(candidate) == id) {
                return candidate;
            }
        }

        return null;
    }

    internal static Type? ResolveOwner(Assembly assembly, string id) {
        if (id.Length < 3 || id[1] != ':') {
            return null;
        }

        string body = id.Substring(2);
        if (id[0] == 'T') {
            return FindType(assembly, body);
        }

        int open = body.IndexOf('(');
        string head = open < 0 ? body : body.Substring(0, open);
        int dot = head.LastIndexOf('.');
        return dot <= 0 ? null : FindType(assembly, head.Substring(0, dot));
    }

    private static string Of(MemberInfo member) {
        StringBuilder id = new StringBuilder();
        switch (member) {
            case Type type:
                id.Append("T:");
                AppendDeclaration(id, type);
                break;
            case FieldInfo field:
                AppendMember(id, "F:", field);
                break;
            case EventInfo @event:
                AppendMember(id, "E:", @event);
                break;
            case PropertyInfo property:
                AppendMember(id, "P:", property);
                AppendParameters(id, property.GetIndexParameters());
                break;
            case MethodBase method:
                AppendMember(id, "M:", method);
                if (method.IsGenericMethod) {
                    id.Append("``").Append(method.GetGenericArguments().Length);
                }

                AppendParameters(id, method.GetParameters());
                if (method is MethodInfo info && IsConversion(info)) {
                    id.Append('~');
                    AppendSignature(id, info.ReturnType);
                }

                break;
        }

        return id.ToString();
    }

    private static Type? FindType(Assembly assembly, string name) {
        if (name.Length == 0) {
            return null;
        }

        char[] nested = name.ToCharArray();
        Type? type = assembly.GetType(name, false);
        int dot = name.Length;
        while (type is null) {
            dot = dot > 0 ? name.LastIndexOf('.', dot - 1) : -1;
            if (dot <= 0) {
                return null;
            }

            nested[dot] = '+';
            type = assembly.GetType(new string(nested), false);
        }

        return type;
    }

    private static void AppendMember(StringBuilder id, string kind, MemberInfo member) {
        id.Append(kind);
        AppendDeclaration(id, member.DeclaringType!);
        id.Append('.').Append(NameOf(member));
    }

    private static void AppendDeclaration(StringBuilder id, Type type) {
        if (type.IsNested) {
            AppendDeclaration(id, type.DeclaringType!);
            id.Append('.');
        } else if (!string.IsNullOrEmpty(type.Namespace)) {
            id.Append(type.Namespace).Append('.');
        }

        id.Append(type.Name);
    }

    private static void AppendParameters(StringBuilder id, ParameterInfo[] parameters) {
        if (parameters.Length == 0) {
            return;
        }

        id.Append('(');
        for (int i = 0; i < parameters.Length; i++) {
            if (i > 0) {
                id.Append(',');
            }

            AppendSignature(id, parameters[i].ParameterType);
        }

        id.Append(')');
    }

    private static void AppendSignature(StringBuilder id, Type type) {
        if (IsFunctionPointer(type)) {
            id.Append("System.IntPtr");
        } else if (type.IsByRef) {
            AppendSignature(id, type.GetElementType()!);
            id.Append('@');
        } else if (type.IsPointer) {
            AppendSignature(id, type.GetElementType()!);
            id.Append('*');
        } else if (type.IsArray) {
            Type element = type.GetElementType()!;
            AppendSignature(id, element);
            AppendRank(id, type, element);
        } else if (type.IsGenericParameter) {
            id.Append(type.DeclaringMethod is null ? "`" : "``").Append(type.GenericParameterPosition);
        } else if (type.IsGenericType) {
            AppendConstructed(id, type.IsGenericTypeDefinition ? type : type.GetGenericTypeDefinition(), type.GetGenericArguments());
        } else {
            AppendDeclaration(id, type);
        }
    }

    private static void AppendRank(StringBuilder id, Type array, Type element) {
        if (array == element.MakeArrayType()) {
            id.Append("[]");
            return;
        }

        id.Append("[0:");
        for (int i = 1; i < array.GetArrayRank(); i++) {
            id.Append(",0:");
        }

        id.Append(']');
    }

    private static void AppendConstructed(StringBuilder id, Type definition, Type[] arguments) {
        int outer = 0;
        if (definition.IsNested) {
            Type parent = definition.DeclaringType!;
            if (parent.IsGenericType) {
                outer = parent.GetGenericArguments().Length;
                AppendConstructed(id, parent, arguments);
            } else {
                AppendDeclaration(id, parent);
            }

            id.Append('.');
        } else if (!string.IsNullOrEmpty(definition.Namespace)) {
            id.Append(definition.Namespace).Append('.');
        }

        string name = definition.Name;
        int tick = name.IndexOf('`');
        id.Append(tick < 0 ? name : name.Substring(0, tick));

        int own = definition.GetGenericArguments().Length;
        if (own == outer) {
            return;
        }

        id.Append('{');
        for (int i = outer; i < own; i++) {
            if (i > outer) {
                id.Append(',');
            }

            AppendSignature(id, arguments[i]);
        }

        id.Append('}');
    }

    private static bool IsFunctionPointer(Type type) {
#if NET
        return type.IsFunctionPointer;
#else
        return type.FullName == "System.MonoFNPtrFakeClass";
#endif
    }

    private static bool IsConversion(MethodInfo method) {
        return method.IsSpecialName && method.Name is "op_Implicit" or "op_Explicit" or "op_CheckedExplicit";
    }

    private static string NameOf(MemberInfo member) {
        return member is FieldInfo ? member.Name : member.Name.Replace('.', '#').Replace('<', '{').Replace('>', '}');
    }
}
