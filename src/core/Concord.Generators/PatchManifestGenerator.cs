using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Concord.Generators;

/// <summary>
///     Writes <c>Concord.Generated.PatchManifest</c>, which hands each Concord attribute of the project to
///     Concord at run time, so the attributes do not have to be in the assembly's metadata.
/// </summary>
[Generator]
public sealed class PatchManifestGenerator : IIncrementalGenerator {
    internal static readonly DiagnosticDescriptor Unwritable = new DiagnosticDescriptor(
        "CONC187",
        "Attribute cannot go into the patch manifest",
        "Concord cannot write [{0}] on '{1}' into the patch manifest: {2}",
        "Concord.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly string[] AttributeNames = [
        "Concord.PatchAttribute",
        "Concord.PatchBeforeAttribute",
        "Concord.PatchAfterAttribute",
        "Concord.PatchDebugAttribute",
        "Concord.PatchRegistryAttribute",
        "Concord.InjectAttribute",
        "Concord.InjectNewAttribute",
        "Concord.SliceAttribute",
        "Concord.LocalAttribute",
        "Concord.CaptureAttribute",
        "Concord.BoundAttribute",
        "Concord.ShadowAttribute",
        "Concord.InjectInstanceAttribute",
        "Concord.InjectFieldAttribute",
        "Concord.InjectPropertyAttribute",
        "Concord.InjectMethodAttribute",
        "Concord.AttachedAttribute",
        "Concord.EnumMemberAttribute",
    ];

    private static readonly SymbolDisplayFormat TypeName = SymbolDisplayFormat.FullyQualifiedFormat;

    private enum EntryKind {
        Assembly,
        Member,
        Parameter,
    }

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context) {
        IncrementalValueProvider<ImmutableArray<ManifestEntry>> all = context.SyntaxProvider
            .CreateSyntaxProvider(static (node, _) => IsRetargeted(node), static (ctx, _) => DescribeRetargeted(ctx))
            .SelectMany(static (entries, _) => entries)
            .Collect();
        IncrementalValueProvider<ImmutableArray<ManifestEntry>> inherited = context.SyntaxProvider
            .CreateSyntaxProvider(static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax, static (ctx, _) => DescribeInherited(ctx))
            .SelectMany(static (entries, _) => entries)
            .Collect();
        all = all.Combine(inherited).Select(static (pair, _) => pair.Left.AddRange(pair.Right));
        foreach (string name in AttributeNames) {
            IncrementalValueProvider<ImmutableArray<ManifestEntry>> uses = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    name,
                    static (_, _) => true,
                    static (ctx, _) => Describe(ctx.TargetSymbol, ctx.TargetNode, ctx.Attributes, ctx.SemanticModel.Compilation))
                .SelectMany(static (entries, _) => entries)
                .Collect();
            all = all.Combine(uses).Select(static (pair, _) => pair.Left.AddRange(pair.Right));
        }

        context.RegisterSourceOutput(all, static (productionContext, entries) => Emit(productionContext, entries));
    }

    internal static string MetadataName(INamedTypeSymbol type) {
        string name = type.MetadataName;
        INamedTypeSymbol outer = type;
        while (outer.ContainingType is { } container) {
            name = container.MetadataName + "+" + name;
            outer = container;
        }

        return outer.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() + "." + name : name;
    }

    internal static bool InFile(ITypeSymbol type) {
        return type switch {
            IArrayTypeSymbol array => InFile(array.ElementType),
            IPointerTypeSymbol pointer => InFile(pointer.PointedAtType),
            INamedTypeSymbol named => named.IsFileLocal ||
                                      (named.ContainingType is { } container && InFile(container)) ||
                                      named.TypeArguments.Any(InFile),
            _ => false,
        };
    }

    private static bool IsRetargeted(SyntaxNode node) {
        return node is AttributeListSyntax { Target.Identifier.ValueText: "field" or "param" or "property" } list &&
               list.Parent is PropertyDeclarationSyntax or EventFieldDeclarationSyntax or AccessorDeclarationSyntax or
                   ParameterSyntax { Parent.Parent: RecordDeclarationSyntax };
    }

    private static ImmutableArray<ManifestEntry> DescribeRetargeted(GeneratorSyntaxContext ctx) {
        AttributeListSyntax list = (AttributeListSyntax)ctx.Node;
        SemanticModel model = ctx.SemanticModel;
        ImmutableArray<ManifestEntry>.Builder entries = ImmutableArray.CreateBuilder<ManifestEntry>();
        if (list.Parent is EventFieldDeclarationSyntax events && list.Target!.Identifier.ValueText == "field") {
            RefuseEventFields(entries, list, events, model);
            return entries.ToImmutable();
        }

        IEnumerable<ISymbol> holders = list.Parent switch {
            PropertyDeclarationSyntax property => BackingFields(model.GetDeclaredSymbol(property)),
            AccessorDeclarationSyntax accessor => model.GetDeclaredSymbol(accessor) is IMethodSymbol method ? method.Parameters : [],
            ParameterSyntax parameter => RecordMembers(model.GetDeclaredSymbol(parameter)),
            _ => [],
        };

        foreach (ISymbol holder in holders) {
            ImmutableArray<AttributeData> attributes = holder.GetAttributes().Where(attribute =>
                attribute.ApplicationSyntaxReference is { } application &&
                application.SyntaxTree == list.SyntaxTree &&
                list.Span.Contains(application.Span) &&
                Array.IndexOf(AttributeNames, attribute.AttributeClass?.ToDisplayString()) >= 0).ToImmutableArray();
            if (attributes.Length > 0) {
                entries.AddRange(Describe(holder, list, attributes, model.Compilation));
            }
        }

        return entries.ToImmutable();
    }

    private static void RefuseEventFields(
        ImmutableArray<ManifestEntry>.Builder entries,
        AttributeListSyntax list,
        EventFieldDeclarationSyntax events,
        SemanticModel model) {
        foreach (AttributeSyntax attribute in list.Attributes) {
            if (model.GetSymbolInfo(attribute).Symbol?.ContainingType is not { } type || Array.IndexOf(AttributeNames, type.ToDisplayString()) < 0) {
                continue;
            }

            foreach (VariableDeclaratorSyntax variable in events.Declaration.Variables) {
                entries.Add(ManifestEntry.Error(Diagnostic.Create(
                    Unwritable,
                    attribute.GetLocation(),
                    type.Name.Replace("Attribute", string.Empty),
                    model.GetDeclaredSymbol(variable)?.ToDisplayString() ?? variable.Identifier.ValueText,
                    "a generator has no view of the field behind a field-like event")));
            }
        }
    }

    private static IEnumerable<ISymbol> BackingFields(ISymbol? associated) {
        return associated?.ContainingType.GetMembers().Where(member =>
                   member is IFieldSymbol field && SymbolEqualityComparer.Default.Equals(field.AssociatedSymbol, associated)) ??
               [];
    }

    private static IEnumerable<ISymbol> RecordMembers(IParameterSymbol? parameter) {
        if (parameter?.ContainingType is not { } record) {
            return [];
        }

        List<ISymbol> members = [];
        foreach (ISymbol property in record.GetMembers(parameter.Name).Where(member => member is IPropertySymbol)) {
            members.Add(property);
            members.AddRange(BackingFields(property));
        }

        return members;
    }

    private static ImmutableArray<ManifestEntry> DescribeInherited(GeneratorSyntaxContext ctx) {
        if (ctx.SemanticModel.GetDeclaredSymbol(ctx.Node) is not INamedTypeSymbol type ||
            type.DeclaringSyntaxReferences.Length == 0 ||
            type.DeclaringSyntaxReferences[0].SyntaxTree != ctx.Node.SyntaxTree ||
            type.DeclaringSyntaxReferences[0].Span != ctx.Node.Span) {
            return [];
        }

        ImmutableArray<ManifestEntry>.Builder entries = ImmutableArray.CreateBuilder<ManifestEntry>();
        Compilation compilation = ctx.SemanticModel.Compilation;
        AddInherited(entries, type, ctx.Node, compilation);
        foreach (ISymbol member in type.GetMembers()) {
            if (member is IPropertySymbol { IsOverride: true }) {
                AddInherited(entries, member, ctx.Node, compilation);
            } else if (member is IMethodSymbol { IsOverride: true } method) {
                AddInherited(entries, method, ctx.Node, compilation);
                foreach (IParameterSymbol parameter in method.Parameters) {
                    AddInherited(entries, parameter, ctx.Node, compilation);
                }
            }
        }

        return entries.ToImmutable();
    }

    private static void AddInherited(ImmutableArray<ManifestEntry>.Builder entries, ISymbol target, SyntaxNode node, Compilation compilation) {
        HashSet<INamedTypeSymbol> present = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (AttributeData own in target.GetAttributes()) {
            if (own.AttributeClass is { } type) {
                present.Add(type);
            }
        }

        int depth = 0;
        for (ISymbol? ancestor = Parent(target); ancestor is not null; ancestor = Parent(ancestor)) {
            depth++;
            ImmutableArray<AttributeData>.Builder level = ImmutableArray.CreateBuilder<AttributeData>();
            foreach (AttributeData attribute in ancestor.GetAttributes()) {
                if (attribute.AttributeClass is { } type &&
                    Array.IndexOf(AttributeNames, type.ToDisplayString()) >= 0 &&
                    Inherits(type, out bool allowMultiple) &&
                    (allowMultiple || !present.Contains(type))) {
                    level.Add(attribute);
                }
            }

            foreach (AttributeData attribute in level) {
                present.Add(attribute.AttributeClass!);
            }

            if (level.Count > 0) {
                entries.AddRange(Describe(target, node, level.ToImmutable(), compilation, depth));
            }
        }
    }

    private static ISymbol? Parent(ISymbol symbol) {
        return symbol switch {
            INamedTypeSymbol type => type.BaseType,
            IMethodSymbol method => method.OverriddenMethod,
            IPropertySymbol property => property.OverriddenProperty,
            IParameterSymbol { ContainingSymbol: IMethodSymbol { OverriddenMethod: { } overridden } } parameter
                when parameter.Ordinal < overridden.Parameters.Length => overridden.Parameters[parameter.Ordinal],
            _ => null,
        };
    }

    private static bool Inherits(INamedTypeSymbol attributeClass, out bool allowMultiple) {
        bool inherited = true;
        allowMultiple = false;
        foreach (AttributeData usage in attributeClass.GetAttributes()) {
            if (usage.AttributeClass?.ToDisplayString() != "System.AttributeUsageAttribute") {
                continue;
            }

            foreach (KeyValuePair<string, TypedConstant> named in usage.NamedArguments) {
                if (named.Key == nameof(AttributeUsageAttribute.Inherited) && named.Value.Value is bool inherits) {
                    inherited = inherits;
                } else if (named.Key == nameof(AttributeUsageAttribute.AllowMultiple) && named.Value.Value is bool multiple) {
                    allowMultiple = multiple;
                }
            }
        }

        return inherited;
    }

    private static ImmutableArray<ManifestEntry> Describe(
        ISymbol target,
        SyntaxNode node,
        ImmutableArray<AttributeData> attributes,
        Compilation compilation,
        int depth = 0) {
        ImmutableArray<ManifestEntry>.Builder entries = ImmutableArray.CreateBuilder<ManifestEntry>();
        string? placement = Place(target, out string group, out EntryKind kind, out string? memberId, out int position);
        for (int index = 0; index < attributes.Length; index++) {
            AttributeData attribute = attributes[index];
            string? failure = placement;
            SyntaxReference? application = attribute.ApplicationSyntaxReference;
            string file = application?.SyntaxTree.FilePath ?? string.Empty;
            int start = application?.Span.Start ?? 0;
            Location location = application is null
                ? node.GetLocation()
                : Location.Create(application.SyntaxTree, application.Span);

            string? construction = null;
            if (failure is null) {
                ArgumentWriter writer = new ArgumentWriter(compilation);
                construction = writer.Construction(attribute);
                failure = writer.Failure;
            }

            if (failure is not null || construction is null) {
                entries.Add(ManifestEntry.Error(Diagnostic.Create(
                    Unwritable,
                    location,
                    attribute.AttributeClass?.Name.Replace("Attribute", string.Empty),
                    target.ToDisplayString(),
                    failure)));
                continue;
            }

            entries.Add(new ManifestEntry(group, kind, memberId, position, construction, file, start, depth == 0 ? 0 : index, depth));
            if (depth == 0 && attribute.AttributeClass?.ToDisplayString() == "Concord.ShadowAttribute" && target is INamedTypeSymbol declaration) {
                AddShadowMember(entries, declaration, attribute, compilation, file, start);
            }
        }

        return entries.ToImmutable();
    }

    private static void AddShadowMember(
        ImmutableArray<ManifestEntry>.Builder entries,
        INamedTypeSymbol declaration,
        AttributeData shadow,
        Compilation compilation,
        string file,
        int start) {
        if (!ShadowMemberModel.IsPartial(declaration) || TargetResolution.ResolveTarget(declaration, compilation) is not { } target) {
            return;
        }

        ShadowMember? member = ShadowMemberModel.Build(declaration, target, shadow, Location.None, []);
        if (member is null) {
            return;
        }

        string construction = "new global::Concord." + member.AttributeName + "Attribute(" +
                              SymbolDisplay.FormatLiteral(member.TargetName, true) + ")";
        entries.Add(new ManifestEntry(IdOf(declaration), EntryKind.Member, member.MemberId, -1, construction, file, start, 1, 0));
    }

    private static string IdOf(ISymbol symbol) {
        StringBuilder id = new StringBuilder();
        switch (symbol) {
            case INamedTypeSymbol type:
                id.Append("T:");
                AppendDeclaration(id, type);
                break;
            case IFieldSymbol:
                AppendMember(id, "F:", symbol);
                break;
            case IEventSymbol:
                AppendMember(id, "E:", symbol);
                break;
            case IPropertySymbol property:
                AppendMember(id, "P:", symbol);
                AppendParameters(id, property.Parameters);
                break;
            case IMethodSymbol method:
                AppendMember(id, "M:", symbol);
                if (method.Arity > 0) {
                    id.Append("``").Append(method.Arity);
                }

                AppendParameters(id, method.Parameters);
                if (method.MethodKind == MethodKind.Conversion) {
                    id.Append('~');
                    AppendSignature(id, method.ReturnType);
                }

                break;
        }

        return id.ToString();
    }

    private static void AppendMember(StringBuilder id, string kind, ISymbol member) {
        id.Append(kind);
        AppendDeclaration(id, member.ContainingType);
        string name = member.MetadataName;
        id.Append('.').Append(member is IFieldSymbol ? name : name.Replace('.', '#').Replace('<', '{').Replace('>', '}'));
    }

    private static void AppendDeclaration(StringBuilder id, INamedTypeSymbol type) {
        if (type.ContainingType is { } container) {
            AppendDeclaration(id, container);
            id.Append('.');
        } else if (type.ContainingNamespace is { IsGlobalNamespace: false } ns) {
            id.Append(ns.ToDisplayString()).Append('.');
        }

        id.Append(type.MetadataName);
    }

    private static void AppendParameters(StringBuilder id, ImmutableArray<IParameterSymbol> parameters) {
        if (parameters.Length == 0) {
            return;
        }

        id.Append('(');
        for (int i = 0; i < parameters.Length; i++) {
            if (i > 0) {
                id.Append(',');
            }

            AppendSignature(id, parameters[i].Type);
            if (parameters[i].RefKind != RefKind.None) {
                id.Append('@');
            }
        }

        id.Append(')');
    }

    private static void AppendSignature(StringBuilder id, ITypeSymbol type) {
        switch (type) {
            case IPointerTypeSymbol pointer:
                AppendSignature(id, pointer.PointedAtType);
                id.Append('*');
                break;
            case IArrayTypeSymbol array:
                AppendSignature(id, array.ElementType);
                id.Append(array.IsSZArray ? "[]" : "[0:" + string.Concat(Enumerable.Repeat(",0:", array.Rank - 1)) + "]");
                break;
            case ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method } parameter:
                id.Append("``").Append(parameter.Ordinal);
                break;
            case ITypeParameterSymbol parameter:
                int position = parameter.Ordinal;
                for (INamedTypeSymbol? outer = parameter.DeclaringType?.ContainingType; outer is not null; outer = outer.ContainingType) {
                    position += outer.Arity;
                }

                id.Append('`').Append(position);
                break;
            case INamedTypeSymbol named:
                AppendNamed(id, named.TupleUnderlyingType ?? named);
                break;
            case IDynamicTypeSymbol:
                id.Append("System.Object");
                break;
            case IFunctionPointerTypeSymbol:
                id.Append("System.IntPtr");
                break;
            default:
                id.Append(type.ToDisplayString());
                break;
        }
    }

    private static void AppendNamed(StringBuilder id, INamedTypeSymbol type) {
        if (type.ContainingType is { } container) {
            AppendNamed(id, container);
            id.Append('.');
        } else if (type.ContainingNamespace is { IsGlobalNamespace: false } ns) {
            id.Append(ns.ToDisplayString()).Append('.');
        }

        string name = type.MetadataName;
        int tick = name.IndexOf('`');
        id.Append(tick < 0 ? name : name.Substring(0, tick));
        if (type.Arity == 0) {
            return;
        }

        id.Append('{');
        for (int i = 0; i < type.TypeArguments.Length; i++) {
            if (i > 0) {
                id.Append(',');
            }

            AppendSignature(id, type.TypeArguments[i]);
        }

        id.Append('}');
    }

    private static string? Place(ISymbol target, out string group, out EntryKind kind, out string? memberId, out int position) {
        group = string.Empty;
        kind = EntryKind.Member;
        memberId = null;
        position = -1;
        switch (target) {
            case IAssemblySymbol:
                kind = EntryKind.Assembly;
                return null;
            case IParameterSymbol parameter:
                if (parameter.ContainingSymbol is not IMethodSymbol method || !IsDescribable(method)) {
                    return "a parameter is described only on an ordinary method, constructor or accessor of a type";
                }

                group = IdOf(method.ContainingType);
                kind = EntryKind.Parameter;
                memberId = IdOf(method);
                position = parameter.Ordinal;
                return null;
            case INamedTypeSymbol type:
                group = IdOf(type);
                memberId = group;
                return null;
            default:
                if (target.ContainingType is null || (target is IMethodSymbol member && !IsDescribable(member))) {
                    return "the member has no documentation comment ID, as a local function or a lambda does not";
                }

                group = IdOf(target.ContainingType);
                memberId = IdOf(target);
                return null;
        }
    }

    private static bool IsDescribable(IMethodSymbol method) {
        return method.ContainingType is not null &&
               method.MethodKind is not (MethodKind.LocalFunction or MethodKind.AnonymousFunction);
    }

    private static void Emit(SourceProductionContext context, ImmutableArray<ManifestEntry> entries) {
        List<ManifestEntry> described = [];
        foreach (ManifestEntry entry in entries) {
            if (entry.Problem is not null) {
                context.ReportDiagnostic(entry.Problem);
            } else {
                described.Add(entry);
            }
        }

        if (described.Count == 0) {
            return;
        }

        described.Sort(static (left, right) => {
            int order = string.CompareOrdinal(left.Group, right.Group);
            if (order == 0) {
                order = left.Depth.CompareTo(right.Depth);
            }

            if (order == 0) {
                order = string.CompareOrdinal(left.File, right.File);
            }

            if (order == 0) {
                order = left.Start.CompareTo(right.Start);
            }

            if (order == 0) {
                order = left.Order.CompareTo(right.Order);
            }

            if (order == 0) {
                order = string.CompareOrdinal(left.MemberId, right.MemberId);
            }

            return order == 0 ? left.Position.CompareTo(right.Position) : order;
        });

        StringBuilder describe = new StringBuilder();
        StringBuilder helpers = new StringBuilder();
        int index = 0;
        int count = 0;
        while (index < described.Count) {
            int end = index;
            while (end < described.Count && described[end].Group == described[index].Group) {
                end++;
            }

            string group = described[index].Group;
            string helper = group.Length == 0 ? "DescribeAssembly" : "Describe" + ++count;
            WriteHelper(helpers, helper, described, index, end);
            if (group.Length == 0) {
                describe.AppendLine("            " + helper + "(sink);");
            } else {
                describe.AppendLine("            try {");
                describe.AppendLine("                " + helper + "(sink);");
                describe.AppendLine("            } catch (global::System.Exception error) when (error is global::System.TypeLoadException || error is global::System.IO.FileNotFoundException || error is global::System.IO.FileLoadException) {");
                describe.AppendLine("                ((global::Concord.IPatchManifestSink)sink).OnUnreadable(" + SymbolDisplay.FormatLiteral(group, true) + ", error);");
                describe.AppendLine("            }");
            }

            index = end;
        }

        StringBuilder source = new StringBuilder();
        source.AppendLine("// <auto-generated/>");
        source.AppendLine("namespace Concord.Generated {");
        source.AppendLine("    internal static class PatchManifest {");
        source.AppendLine("        public static void Describe(object sink) {");
        source.Append(describe);
        source.AppendLine("        }");
        source.Append(helpers);
        source.AppendLine("    }");
        source.AppendLine("}");
        context.AddSource("ConcordPatchManifest.g.cs", SourceText.From(source.ToString(), Encoding.UTF8));
    }

    private static void WriteHelper(StringBuilder helpers, string name, List<ManifestEntry> entries, int start, int end) {
        helpers.AppendLine();
        helpers.AppendLine("        private static void " + name + "(object sink) {");
        for (int i = start; i < end; i++) {
            helpers.AppendLine("            global::System.Attribute a" + (i - start) + " = " + entries[i].Construction + ";");
        }

        helpers.AppendLine("            global::Concord.IPatchManifestSink s = (global::Concord.IPatchManifestSink)sink;");
        for (int i = start; i < end; i++) {
            ManifestEntry entry = entries[i];
            string attribute = "a" + (i - start);
            string id = SymbolDisplay.FormatLiteral(entry.MemberId ?? string.Empty, true);
            helpers.AppendLine(entry.Kind switch {
                EntryKind.Assembly => "            s.OnAssembly(" + attribute + ");",
                EntryKind.Parameter => "            s.OnParameter(" + id + ", " + entry.Position.ToString(CultureInfo.InvariantCulture) + ", " + attribute + ");",
                _ => "            s.OnMember(" + id + ", " + attribute + ");",
            });
        }

        helpers.AppendLine("        }");
    }

    private static bool ContainsPointer(ITypeSymbol type) {
        return type switch {
            IPointerTypeSymbol or IFunctionPointerTypeSymbol => true,
            IArrayTypeSymbol array => ContainsPointer(array.ElementType),
            INamedTypeSymbol named => named.TypeArguments.Any(ContainsPointer),
            _ => false,
        };
    }

    private static ulong Bits(object value) {
        return value switch {
            sbyte v => unchecked((ulong)v),
            short v => unchecked((ulong)v),
            int v => unchecked((ulong)v),
            long v => unchecked((ulong)v),
            _ => Convert.ToUInt64(value, CultureInfo.InvariantCulture),
        };
    }

    private static string Single(float value) {
        if (float.IsNaN(value)) {
            return "float.NaN";
        }

        if (float.IsInfinity(value)) {
            return value > 0 ? "float.PositiveInfinity" : "float.NegativeInfinity";
        }

        return value.ToString("G9", CultureInfo.InvariantCulture) + "F";
    }

    private static string Double(double value) {
        if (double.IsNaN(value)) {
            return "double.NaN";
        }

        if (double.IsInfinity(value)) {
            return value > 0 ? "double.PositiveInfinity" : "double.NegativeInfinity";
        }

        return value.ToString("G17", CultureInfo.InvariantCulture) + "D";
    }

    private sealed class ManifestEntry(
        string group,
        EntryKind kind,
        string? memberId,
        int position,
        string construction,
        string file,
        int start,
        int order,
        int depth,
        Diagnostic? problem = null) {
        public string Group { get; } = group;

        public EntryKind Kind { get; } = kind;

        public string? MemberId { get; } = memberId;

        public int Position { get; } = position;

        public string Construction { get; } = construction;

        public string File { get; } = file;

        public int Start { get; } = start;

        public int Order { get; } = order;

        public int Depth { get; } = depth;

        public Diagnostic? Problem { get; } = problem;

        public static ManifestEntry Error(Diagnostic problem) {
            return new ManifestEntry(string.Empty, EntryKind.Member, null, -1, string.Empty, string.Empty, 0, 0, 0, problem);
        }
    }

    private sealed class ArgumentWriter(Compilation compilation) {
        public string? Failure { get; private set; }

        public string? Construction(AttributeData attribute) {
            if (attribute.AttributeClass is not { } type || attribute.AttributeConstructor is not { } constructor) {
                return Fail("the attribute does not bind to a constructor");
            }

            StringBuilder code = new StringBuilder("new ").Append(type.ToDisplayString(TypeName)).Append('(');
            ImmutableArray<TypedConstant> arguments = attribute.ConstructorArguments;
            for (int i = 0; i < arguments.Length; i++) {
                if (i > 0) {
                    code.Append(", ");
                }

                ITypeSymbol slot = constructor.Parameters[i].Type;
                string? value = Value(arguments[i], slot);
                code.Append(slot.SpecialType == SpecialType.System_Object && !arguments[i].IsNull ? "(object)(" + value + ")" : value);
            }

            code.Append(')');
            if (attribute.NamedArguments.Length > 0) {
                code.Append(" { ");
                for (int i = 0; i < attribute.NamedArguments.Length; i++) {
                    KeyValuePair<string, TypedConstant> named = attribute.NamedArguments[i];
                    if (i > 0) {
                        code.Append(", ");
                    }

                    code.Append(named.Key).Append(" = ").Append(Value(named.Value, named.Value.Type));
                }

                code.Append(" }");
            }

            return Failure is null ? code.ToString() : null;
        }

        private string? Value(TypedConstant constant, ITypeSymbol? slot) {
            if (constant.Kind == TypedConstantKind.Error) {
                return Fail("an argument does not compile");
            }

            if (constant.IsNull) {
                return slot is null || slot.SpecialType == SpecialType.System_Object ? "null" : "default(" + Writable(slot) + ")";
            }

            switch (constant.Kind) {
                case TypedConstantKind.Array:
                    ITypeSymbol element = ((IArrayTypeSymbol)constant.Type!).ElementType;
                    StringBuilder array = new StringBuilder("new ").Append(Writable(element)).Append("[] {");
                    for (int i = 0; i < constant.Values.Length; i++) {
                        array.Append(i > 0 ? ", " : " ").Append(Value(constant.Values[i], element));
                    }

                    return array.Append(" }").ToString();
                case TypedConstantKind.Type:
                    return TypeOf((ITypeSymbol)constant.Value!);
                case TypedConstantKind.Enum:
                    return EnumValue((INamedTypeSymbol)constant.Type!, constant.Value!);
                default:
                    return Literal(constant.Value!);
            }
        }

        private string? Writable(ITypeSymbol type) {
            return IsAccessible(type) ? type.ToDisplayString(TypeName) : Fail("the type '" + type.ToDisplayString() + "' is not visible to the manifest");
        }

        private bool IsAccessible(ITypeSymbol type) {
            return compilation.IsSymbolAccessibleWithin(type, compilation.Assembly) && !InFile(type);
        }

        private string? TypeOf(ITypeSymbol type) {
            switch (type) {
                case IPointerTypeSymbol pointer:
                    return TypeOf(pointer.PointedAtType) + ".MakePointerType()";
                case IArrayTypeSymbol when IsAccessible(type) && !ContainsPointer(type):
                    return "typeof(" + type.ToDisplayString(TypeName) + ")";
                case IArrayTypeSymbol array:
                    return TypeOf(array.ElementType) + (array.IsSZArray ? ".MakeArrayType()" : ".MakeArrayType(" + array.Rank + ")");
                case INamedTypeSymbol named when named.TypeKind != TypeKind.Error:
                    return NamedTypeOf(named);
                default:
                    return Fail("the type '" + type.ToDisplayString() + "' cannot be written as a type");
            }
        }

        private string? NamedTypeOf(INamedTypeSymbol type) {
            if (IsAccessible(type) && !ContainsPointer(type)) {
                return "typeof(" + type.ToDisplayString(TypeName) + ")";
            }

            if (type.IsGenericType && !type.IsUnboundGenericType && !SymbolEqualityComparer.Default.Equals(type, type.OriginalDefinition)) {
                StringBuilder generic = new StringBuilder(DefinitionOf(type.OriginalDefinition)).Append(".MakeGenericType(");
                List<ITypeSymbol> arguments = [];
                for (INamedTypeSymbol? container = type; container is not null; container = container.ContainingType) {
                    arguments.InsertRange(0, container.TypeArguments);
                }

                for (int i = 0; i < arguments.Count; i++) {
                    generic.Append(i > 0 ? ", " : string.Empty).Append(TypeOf(arguments[i]));
                }

                return generic.Append(')').ToString();
            }

            return DefinitionOf(type.OriginalDefinition);
        }

        private string? DefinitionOf(INamedTypeSymbol definition) {
            if (IsAccessible(definition)) {
                return "typeof(" + (definition.IsGenericType ? definition.ConstructUnboundGenericType() : definition).ToDisplayString(TypeName) + ")";
            }

            string anchor;
            if (SymbolEqualityComparer.Default.Equals(definition.ContainingAssembly, compilation.Assembly)) {
                anchor = "typeof(global::Concord.Generated.PatchManifest)";
            } else {
                INamedTypeSymbol? container = definition.ContainingType;
                while (container is not null && !IsAccessible(container)) {
                    container = container.ContainingType;
                }

                if (container is null) {
                    return Fail("the type '" + definition.ToDisplayString() + "' is not visible to the manifest");
                }

                anchor = DefinitionOf(container.OriginalDefinition)!;
            }

            return anchor + ".Assembly.GetType(" + SymbolDisplay.FormatLiteral(MetadataName(definition), true) + ", true)";
        }

        private string? EnumValue(INamedTypeSymbol type, object value) {
            if (!IsAccessible(type)) {
                return "global::System.Enum.ToObject(" + TypeOf(type) + ", " + Literal(value) + ")";
            }

            string name = type.ToDisplayString(TypeName);
            ulong bits = Bits(value);
            List<(string Name, ulong Bits)> members = [];
            foreach (ISymbol member in type.GetMembers()) {
                if (member is IFieldSymbol { HasConstantValue: true, ConstantValue: { } constant } field) {
                    if (Bits(constant) == bits) {
                        return name + "." + field.Name;
                    }

                    members.Add((field.Name, Bits(constant)));
                }
            }

            members.Sort(static (left, right) => right.Bits.CompareTo(left.Bits));
            ulong remaining = bits;
            List<string> flags = [];
            foreach ((string flag, ulong flagBits) in members) {
                if (flagBits != 0 && (flagBits & remaining) == flagBits) {
                    flags.Insert(0, name + "." + flag);
                    remaining &= ~flagBits;
                }
            }

            return bits != 0 && remaining == 0
                ? string.Join(" | ", flags)
                : "(" + name + ")(" + Literal(value) + ")";
        }

        private string? Literal(object value) {
            return value switch {
                string text => SymbolDisplay.FormatLiteral(text, true),
                char character => SymbolDisplay.FormatLiteral(character, true),
                bool flag => flag ? "true" : "false",
                byte v => "(byte)" + v.ToString(CultureInfo.InvariantCulture),
                sbyte v => "(sbyte)(" + v.ToString(CultureInfo.InvariantCulture) + ")",
                short v => "(short)(" + v.ToString(CultureInfo.InvariantCulture) + ")",
                ushort v => "(ushort)" + v.ToString(CultureInfo.InvariantCulture),
                int v => v.ToString(CultureInfo.InvariantCulture),
                uint v => v.ToString(CultureInfo.InvariantCulture) + "U",
                long v => v.ToString(CultureInfo.InvariantCulture) + "L",
                ulong v => v.ToString(CultureInfo.InvariantCulture) + "UL",
                float v => Single(v),
                double v => Double(v),
                _ => Fail("the value '" + value + "' has no literal form"),
            };
        }

        private string? Fail(string reason) {
            Failure ??= reason;
            return null;
        }
    }
}
