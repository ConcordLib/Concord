using System.Reflection;
using System.Runtime.CompilerServices;
using Mono.Cecil;

namespace Concord.Emit;

internal static class Reflected {
    internal const string Unresolvable = "CONC182";

    private const BindingFlags Everything = BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly ConditionalWeakTable<MemberReference, MemberInfo> Known =
        new ConditionalWeakTable<MemberReference, MemberInfo>();

    internal static Type ResolveReflection(this TypeReference reference) {
        return TypeOf(reference) ?? throw Refuse(reference, "type");
    }

    internal static FieldInfo ResolveReflection(this FieldReference reference) {
        return FieldOf(reference) ?? throw Refuse(reference, "field");
    }

    internal static MethodBase ResolveReflection(this MethodReference reference) {
        return MethodOf(reference) ?? throw Refuse(reference, "method");
    }

    internal static void Note(TypeReference reference, Type type) {
        Known.AddOrUpdate(reference, type);
        switch (reference) {
            case ArrayType array when type.IsArray && type.GetElementType() is { } element:
                Note(array.ElementType, element);
                break;
            case ByReferenceType or PointerType when type.HasElementType && type.GetElementType() is { } pointee:
                Note(((TypeSpecification)reference).ElementType, pointee);
                break;
            case GenericInstanceType instance when type.IsGenericType && !type.IsGenericTypeDefinition:
                Note(instance.ElementType, type.GetGenericTypeDefinition());
                Type[] arguments = type.GetGenericArguments();
                for (int i = 0; i < instance.GenericArguments.Count && i < arguments.Length; i++) {
                    Note(instance.GenericArguments[i], arguments[i]);
                }

                break;
            default:
                if (reference.DeclaringType is { } declaring && type.DeclaringType is { } owner) {
                    Note(declaring, owner);
                }

                break;
        }
    }

    internal static void Note(FieldReference reference, FieldInfo field) {
        Known.AddOrUpdate(reference, field);
        if (field.DeclaringType is { } declaring) {
            Note(reference.DeclaringType, declaring);
        }

        Note(reference.FieldType, field.FieldType);
    }

    internal static void Note(MethodReference reference, MethodBase method) {
        Known.AddOrUpdate(reference, method);
        if (method.DeclaringType is { } declaring) {
            Note(reference.DeclaringType, declaring);
        }

        if (reference is GenericInstanceMethod instance
            && method is MethodInfo { IsGenericMethod: true, IsGenericMethodDefinition: false } closed) {
            Note(instance.ElementMethod, closed.GetGenericMethodDefinition());
            Type[] closures = closed.GetGenericArguments();
            for (int i = 0; i < instance.GenericArguments.Count && i < closures.Length; i++) {
                Note(instance.GenericArguments[i], closures[i]);
            }

            return;
        }

        if (method is MethodInfo { ReturnType: { } returned }) {
            Note(reference.ReturnType, returned);
        }

        ParameterInfo[] parameters = method.GetParameters();
        for (int i = 0; i < reference.Parameters.Count && i < parameters.Length; i++) {
            Note(reference.Parameters[i].ParameterType, parameters[i].ParameterType);
        }
    }

    internal static void Copy(MemberReference from, MemberReference to) {
        if (Known.TryGetValue(from, out MemberInfo? member)) {
            Known.AddOrUpdate(to, member);
        }

        switch (from) {
            case GenericInstanceMethod source when to is GenericInstanceMethod copy:
                Copy(source.ElementMethod, copy.ElementMethod);
                CopyAll(source.GenericArguments, copy.GenericArguments);
                break;
            case GenericInstanceType source when to is GenericInstanceType copy:
                Copy(source.ElementType, copy.ElementType);
                CopyAll(source.GenericArguments, copy.GenericArguments);
                break;
            case TypeSpecification source when to is TypeSpecification copy:
                Copy(source.ElementType, copy.ElementType);
                break;
            case MethodReference source when to is MethodReference copy:
                Copy(source.ReturnType, copy.ReturnType);
                for (int i = 0; i < source.Parameters.Count && i < copy.Parameters.Count; i++) {
                    Copy(source.Parameters[i].ParameterType, copy.Parameters[i].ParameterType);
                }

                break;
        }

        if (from.DeclaringType is { } owner && to.DeclaringType is { } clone) {
            Copy(owner, clone);
        }
    }

    internal static Type? TypeOf(TypeReference? reference) {
        switch (reference) {
            case null:
                return null;
            case ArrayType array:
                return TypeOf(array.ElementType) is { } element
                    ? array.IsVector ? element.MakeArrayType() : element.MakeArrayType(array.Rank)
                    : null;
            case ByReferenceType byReference:
                return TypeOf(byReference.ElementType)?.MakeByRefType();
            case PointerType pointer:
                return TypeOf(pointer.ElementType)?.MakePointerType();
            case PinnedType pinned:
                return TypeOf(pinned.ElementType);
            case SentinelType sentinel:
                return TypeOf(sentinel.ElementType);
            case IModifierType modifier:
                return TypeOf(modifier.ElementType);
            case FunctionPointerType:
                return null;
            case GenericInstanceType instance:
                return Instantiate(instance);
            default:
                if (Known.TryGetValue(reference, out MemberInfo? member) && member is Type known) {
                    return known;
                }

                return Primitive(reference) ?? ByName(reference);
        }
    }

    internal static FieldInfo? FieldOf(FieldReference? reference) {
        if (reference is null) {
            return null;
        }

        if (Known.TryGetValue(reference, out MemberInfo? member) && member is FieldInfo known) {
            return known;
        }

        Type? declaring = TypeOf(reference.DeclaringType);
        return declaring?.GetField(reference.Name, Everything)
            ?? declaring?.GetField(reference.Name, Everything & ~BindingFlags.DeclaredOnly);
    }

    internal static MethodBase? MethodOf(MethodReference? reference) {
        if (reference is null) {
            return null;
        }

        if (Known.TryGetValue(reference, out MemberInfo? member) && member is MethodBase known) {
            return known;
        }

        return reference is GenericInstanceMethod instance ? Close(instance) : Match(reference);
    }

    private static void CopyAll(IList<TypeReference> from, IList<TypeReference> to) {
        for (int i = 0; i < from.Count && i < to.Count; i++) {
            Copy(from[i], to[i]);
        }
    }

    private static Type? Primitive(TypeReference reference) {
        return reference.MetadataType switch {
            MetadataType.Void => typeof(void),
            MetadataType.Boolean => typeof(bool),
            MetadataType.Char => typeof(char),
            MetadataType.SByte => typeof(sbyte),
            MetadataType.Byte => typeof(byte),
            MetadataType.Int16 => typeof(short),
            MetadataType.UInt16 => typeof(ushort),
            MetadataType.Int32 => typeof(int),
            MetadataType.UInt32 => typeof(uint),
            MetadataType.Int64 => typeof(long),
            MetadataType.UInt64 => typeof(ulong),
            MetadataType.Single => typeof(float),
            MetadataType.Double => typeof(double),
            MetadataType.IntPtr => typeof(IntPtr),
            MetadataType.UIntPtr => typeof(UIntPtr),
            MetadataType.String => typeof(string),
            MetadataType.Object => typeof(object),
            MetadataType.TypedByReference => typeof(TypedReference),
            _ => null,
        };
    }

    private static Type? ByName(TypeReference reference) {
        if (reference is GenericParameter) {
            return null;
        }

        if (reference.Scope is not AssemblyNameReference scope) {
            return null;
        }

        string name = reference.FullName.Replace('/', '+');
        foreach (Assembly candidate in AppDomain.CurrentDomain.GetAssemblies()) {
            if (candidate.GetName().Name == scope.Name && Lookup(candidate, name) is { } hit) {
                return hit;
            }
        }

        return null;
    }

    private static Type? Lookup(Assembly assembly, string name) {
        try {
            return assembly.GetType(name, false);
        } catch (Exception failure) when (failure is FileNotFoundException or FileLoadException
                                             or BadImageFormatException or TypeLoadException) {
            return null;
        }
    }

    private static Type? Instantiate(GenericInstanceType instance) {
        if (TypeOf(instance.ElementType) is not { } definition) {
            return null;
        }

        Type[] arguments = new Type[instance.GenericArguments.Count];
        for (int i = 0; i < arguments.Length; i++) {
            if (TypeOf(instance.GenericArguments[i]) is not { } argument) {
                return null;
            }

            arguments[i] = argument;
        }

        return definition.IsGenericTypeDefinition ? definition.MakeGenericType(arguments) : definition;
    }

    private static MethodBase? Close(GenericInstanceMethod instance) {
        if (MethodOf(instance.ElementMethod) is not MethodInfo open) {
            return null;
        }

        Type[] arguments = new Type[instance.GenericArguments.Count];
        for (int i = 0; i < arguments.Length; i++) {
            if (TypeOf(instance.GenericArguments[i]) is not { } argument) {
                return null;
            }

            arguments[i] = argument;
        }

        return open.IsGenericMethodDefinition ? open.MakeGenericMethod(arguments) : open;
    }

    private static MethodBase? Match(MethodReference reference) {
        if (TypeOf(reference.DeclaringType) is not { } declaring) {
            return null;
        }

        List<MethodBase> candidates = [];
        foreach (MethodBase candidate in Candidates(declaring, reference.Name)) {
            if (candidate.GetParameters().Length != reference.Parameters.Count) {
                continue;
            }

            int arity = candidate is MethodInfo { IsGenericMethodDefinition: true } generic
                ? generic.GetGenericArguments().Length
                : 0;
            if (arity != reference.GenericParameters.Count) {
                continue;
            }

            candidates.Add(candidate);
        }

        return candidates.Count == 1 ? candidates[0] : Narrow(candidates, reference, declaring);
    }

    private static MethodBase? Narrow(List<MethodBase> candidates, MethodReference reference, Type declaring) {
        MethodBase? found = null;
        foreach (MethodBase candidate in candidates) {
            ParameterInfo[] parameters = candidate.GetParameters();
            bool matches = true;
            for (int i = 0; i < parameters.Length && matches; i++) {
                matches = Matches(reference.Parameters[i].ParameterType, parameters[i].ParameterType, declaring);
            }

            if (!matches) {
                continue;
            }

            if (found is not null) {
                return null;
            }

            found = candidate;
        }

        return found;
    }

    private static bool Matches(TypeReference reference, Type type, Type declaring) {
        switch (reference) {
            case GenericParameter { Type: GenericParameterType.Method } parameter:
                return TypeFacts.IsGenericMethodParameter(type) && type.GenericParameterPosition == parameter.Position;
            case GenericParameter parameter:
                return Owned(parameter, type, declaring);
            case ByReferenceType byReference:
                return type is { IsByRef: true } && type.GetElementType() is { } pointee
                    && Matches(byReference.ElementType, pointee, declaring);
            case ArrayType array:
                return type.IsArray && type.GetArrayRank() == array.Rank && type.GetElementType() is { } item
                    && Matches(array.ElementType, item, declaring);
            case PointerType pointer:
                return type.IsPointer && type.GetElementType() is { } pointed
                    && Matches(pointer.ElementType, pointed, declaring);
            case GenericInstanceType instance:
                return Constructed(instance, type, declaring);
            default:
                return TypeOf(reference) == type;
        }
    }

    private static bool Owned(GenericParameter parameter, Type type, Type declaring) {
        if (!declaring.IsGenericType || declaring.IsGenericTypeDefinition) {
            return TypeFacts.IsGenericTypeParameter(type) && type.GenericParameterPosition == parameter.Position;
        }

        Type[] arguments = declaring.GetGenericArguments();
        return parameter.Position < arguments.Length && arguments[parameter.Position] == type;
    }

    private static bool Constructed(GenericInstanceType instance, Type type, Type declaring) {
        if (!type.IsGenericType || type.IsGenericTypeDefinition) {
            return false;
        }

        Type[] arguments = type.GetGenericArguments();
        if (arguments.Length != instance.GenericArguments.Count
            || TypeOf(instance.ElementType) != type.GetGenericTypeDefinition()) {
            return false;
        }

        for (int i = 0; i < arguments.Length; i++) {
            if (!Matches(instance.GenericArguments[i], arguments[i], declaring)) {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<MethodBase> Candidates(Type declaring, string name) {
        if (name is ".ctor" or ".cctor") {
            return Array.FindAll(
                declaring.GetConstructors(Everything),
                constructor => constructor.IsStatic == (name == ".cctor"));
        }

        List<MethodInfo> named = [];
        for (Type? walk = declaring; walk is not null; walk = walk.BaseType) {
            foreach (MethodInfo method in walk.GetMethods(Everything)) {
                if (method.Name != name || Shadowed(named, method)) {
                    continue;
                }

                named.Add(method);
            }
        }

        return named;
    }

    private static bool Shadowed(List<MethodInfo> derived, MethodInfo method) {
        foreach (MethodInfo kept in derived) {
            if (kept.GetBaseDefinition() == method.GetBaseDefinition() || SameShape(kept, method)) {
                return true;
            }
        }

        return false;
    }

    private static bool SameShape(MethodInfo kept, MethodInfo method) {
        return kept.GetGenericArguments().Length == method.GetGenericArguments().Length
            && kept.GetParameters().Select(parameter => parameter.ParameterType)
                .SequenceEqual(method.GetParameters().Select(parameter => parameter.ParameterType));
    }

    private static ConcordEmitException Refuse(MemberReference reference, string kind) {
        return new ConcordEmitException(
            Unresolvable,
            $"Could not resolve the runtime {kind} '{reference.FullName}' a composed body names. Pass a member from a " +
            "loaded type, and make sure its assembly is loaded before the patch is applied.");
    }
}

internal sealed class RecordingReflectionImporter : IReflectionImporter {
    private readonly DefaultReflectionImporter inner;

    internal RecordingReflectionImporter(ModuleDefinition module) {
        inner = new DefaultReflectionImporter(module);
    }

    public AssemblyNameReference ImportReference(AssemblyName reference) {
        return inner.ImportReference(reference);
    }

    public TypeReference ImportReference(Type type, IGenericParameterProvider? context) {
        TypeReference imported = inner.ImportReference(type, context);
        Reflected.Note(imported, type);
        return imported;
    }

    public FieldReference ImportReference(FieldInfo field, IGenericParameterProvider? context) {
        FieldReference imported = inner.ImportReference(field, context);
        Reflected.Note(imported, field);
        return imported;
    }

    public MethodReference ImportReference(MethodBase method, IGenericParameterProvider? context) {
        MethodReference imported = inner.ImportReference(method, context);
        Reflected.Note(imported, method);
        return imported;
    }
}

internal sealed class RecordingMetadataImporter : IMetadataImporter {
    private readonly DefaultMetadataImporter inner;

    internal RecordingMetadataImporter(ModuleDefinition module) {
        inner = new DefaultMetadataImporter(module);
    }

    public AssemblyNameReference ImportReference(AssemblyNameReference reference) {
        return inner.ImportReference(reference);
    }

    public TypeReference ImportReference(TypeReference type, IGenericParameterProvider? context) {
        TypeReference imported = inner.ImportReference(type, context);
        Reflected.Copy(type, imported);
        return imported;
    }

    public FieldReference ImportReference(FieldReference field, IGenericParameterProvider? context) {
        FieldReference imported = inner.ImportReference(field, context);
        Reflected.Copy(field, imported);
        return imported;
    }

    public MethodReference ImportReference(MethodReference method, IGenericParameterProvider? context) {
        MethodReference imported = inner.ImportReference(method, context);
        Reflected.Copy(method, imported);
        return imported;
    }
}

internal sealed class RecordingImporters : IReflectionImporterProvider, IMetadataImporterProvider {
    public IReflectionImporter GetReflectionImporter(ModuleDefinition module) {
        return new RecordingReflectionImporter(module);
    }

    public IMetadataImporter GetMetadataImporter(ModuleDefinition module) {
        return new RecordingMetadataImporter(module);
    }
}
