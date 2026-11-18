using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Concord.Generators;

internal static class ShadowMemberModel {
    private static readonly SymbolDisplayFormat TypeFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    internal static bool IsPartial(INamedTypeSymbol declaration) {
        foreach (SyntaxReference reference in declaration.DeclaringSyntaxReferences) {
            if (reference.GetSyntax() is ClassDeclarationSyntax { Modifiers: { } modifiers } &&
                modifiers.Any(SyntaxKind.PartialKeyword)) {
                return true;
            }
        }

        return false;
    }

    internal static ShadowMember? Build(
        INamedTypeSymbol declaration,
        INamedTypeSymbol target,
        AttributeData shadow,
        Location location,
        List<Diagnostic> diagnostics) {
        if (shadow.ConstructorArguments.Length == 0 ||
            shadow.ConstructorArguments[0].Value is not string memberName) {
            return null;
        }

        ImmutableArray<TypedConstant> parameterTypes = shadow.ConstructorArguments.Length > 1
            ? shadow.ConstructorArguments[1].Values
            : ImmutableArray<TypedConstant>.Empty;

        ImmutableArray<ISymbol> candidates = FindMembers(target, memberName);
        if (candidates.Length == 0) {
            diagnostics.Add(Diagnostic.Create(ShadowMemberGenerator.MemberNotFound, location, target.Name, memberName));
            return null;
        }

        string owner = declaration.GetDocumentationCommentId()!.Substring(2) + ".";
        if (candidates[0] is IFieldSymbol field) {
            string staticModifier = field.IsStatic ? "static " : string.Empty;
            return new ShadowMember(
                "InjectField",
                field.Name,
                "F:" + owner + field.Name,
                "    #pragma warning disable CS0649\n" +
                "    [global::Concord.InjectField(\"" + field.Name + "\")]\n" +
                "    private " + staticModifier + field.Type.ToDisplayString(TypeFormat) + " " + field.Name + ";\n" +
                "    #pragma warning restore CS0649\n");
        }

        if (!declaration.IsAbstract) {
            diagnostics.Add(Diagnostic.Create(
                ShadowMemberGenerator.NotPartial, location, declaration.Name, " and abstract for method/property shadows"));
            return null;
        }

        Action<string> unsupported = reason => diagnostics.Add(
            Diagnostic.Create(ShadowMemberGenerator.UnsupportedMember, location, target.Name, memberName, reason));
        if (candidates[0] is IPropertySymbol property) {
            return BuildProperty(property, owner, unsupported);
        }

        IMethodSymbol? selected = SelectMethod(candidates, parameterTypes, unsupported, out int overloads);
        if (selected is null) {
            if (overloads > 0) {
                diagnostics.Add(Diagnostic.Create(
                    ShadowMemberGenerator.AmbiguousMember, location, target.Name, memberName, overloads));
            }

            return null;
        }

        return new ShadowMember(
            "InjectMethod",
            selected.Name,
            "M:" + owner + selected.Name + ParameterIds(selected),
            "    [global::Concord.InjectMethod(\"" + selected.Name + "\")]\n" + BuildMethodSignature(selected) + "\n");
    }

    private static ImmutableArray<ISymbol> FindMembers(INamedTypeSymbol target, string name) {
        for (INamedTypeSymbol? type = target; type is not null; type = type.BaseType) {
            ImmutableArray<ISymbol> members = type.GetMembers(name);
            if (members.Length > 0) {
                return members;
            }
        }

        return ImmutableArray<ISymbol>.Empty;
    }

    private static ShadowMember? BuildProperty(IPropertySymbol property, string owner, Action<string> unsupported) {
        if (property.IsStatic) {
            unsupported("static properties are not supported");
            return null;
        }

        if (property.IsIndexer) {
            unsupported("indexers are not supported");
            return null;
        }

        string accessors = (property.GetMethod is not null ? "get; " : string.Empty) +
                           (property.SetMethod is not null ? "set; " : string.Empty);
        return new ShadowMember(
            "InjectProperty",
            property.Name,
            "P:" + owner + property.Name,
            "    [global::Concord.InjectProperty(\"" + property.Name + "\")]\n" +
            "    protected abstract " + property.Type.ToDisplayString(TypeFormat) + " " + property.Name +
            " { " + accessors.TrimEnd() + " }\n");
    }

    private static IMethodSymbol? SelectMethod(
        ImmutableArray<ISymbol> candidates,
        ImmutableArray<TypedConstant> parameterTypes,
        Action<string> unsupported,
        out int ambiguous) {
        ambiguous = 0;
        List<IMethodSymbol> methods = [];
        foreach (ISymbol candidate in candidates) {
            if (candidate is IMethodSymbol { MethodKind: MethodKind.Ordinary } method) {
                methods.Add(method);
            }
        }

        if (methods.Count == 0) {
            unsupported("only fields, properties, and ordinary methods are supported");
            return null;
        }

        IMethodSymbol? selected = SelectOverload(methods, parameterTypes);
        if (selected is null) {
            ambiguous = methods.Count;
            return null;
        }

        if (selected.IsStatic || selected.IsGenericMethod) {
            unsupported("static and generic methods are not supported");
            return null;
        }

        return selected;
    }

    private static string ParameterIds(IMethodSymbol method) {
        if (method.Parameters.Length == 0) {
            return string.Empty;
        }

        StringBuilder id = new StringBuilder("(");
        for (int index = 0; index < method.Parameters.Length; index++) {
            IParameterSymbol parameter = method.Parameters[index];
            if (index > 0) {
                id.Append(',');
            }

            id.Append(DocumentationCommentId.CreateReferenceId(parameter.Type));
            if (parameter.RefKind != RefKind.None) {
                id.Append('@');
            }
        }

        return id.Append(')').ToString();
    }

    private static string BuildMethodSignature(IMethodSymbol selected) {
        StringBuilder signature = new StringBuilder();
        signature.Append("    protected abstract ");
        signature.Append(selected.ReturnsVoid ? "void" : selected.ReturnType.ToDisplayString(TypeFormat));
        signature.Append(' ').Append(selected.Name).Append('(');
        for (int index = 0; index < selected.Parameters.Length; index++) {
            IParameterSymbol parameter = selected.Parameters[index];
            if (index > 0) {
                signature.Append(", ");
            }

            signature.Append(RefKindPrefix(parameter.RefKind));
            signature.Append(parameter.Type.ToDisplayString(TypeFormat));
            signature.Append(' ');
            signature.Append(string.IsNullOrEmpty(parameter.Name) ? "arg" + index : parameter.Name);
        }

        signature.Append(");");
        return signature.ToString();
    }

    private static string RefKindPrefix(RefKind refKind) {
        return refKind switch {
            RefKind.Ref => "ref ",
            RefKind.Out => "out ",
            RefKind.In => "in ",
            _ => string.Empty,
        };
    }

    private static IMethodSymbol? SelectOverload(List<IMethodSymbol> methods, ImmutableArray<TypedConstant> parameterTypes) {
        if (methods.Count == 1 && parameterTypes.IsDefaultOrEmpty) {
            return methods[0];
        }

        if (parameterTypes.IsDefaultOrEmpty) {
            return null;
        }

        foreach (IMethodSymbol method in methods) {
            if (method.Parameters.Length != parameterTypes.Length) {
                continue;
            }

            bool matches = true;
            for (int index = 0; index < parameterTypes.Length; index++) {
                if (parameterTypes[index].Value is not ITypeSymbol requested ||
                    !SymbolEqualityComparer.Default.Equals(requested, method.Parameters[index].Type)) {
                    matches = false;
                    break;
                }
            }

            if (matches) {
                return method;
            }
        }

        return null;
    }
}

internal sealed class ShadowMember(string attributeName, string targetName, string memberId, string source) {
    public string AttributeName { get; } = attributeName;

    public string TargetName { get; } = targetName;

    public string MemberId { get; } = memberId;

    public string Source { get; } = source;
}
