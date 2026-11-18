#if !NET472
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Concord.Backend;
using Concord.Orchestration;

namespace Concord.PackageContract;

public static class Checks {
    public static int Main(string[] args) {
        switch (args.FirstOrDefault()) {
            case "metadata" when args.Length == 3:
                return Metadata(args[1], args[2] == "some");
            case "apply" when args.Length == 1:
                return Apply(typeof(ContractPatch).Assembly);
            case "apply" when args.Length == 2:
                return Apply(Assembly.LoadFrom(args[1]));
            case "merged" when args.Length == 2:
                return Merged(args[1]);
            default:
                Console.Error.WriteLine("usage: metadata <dll> none|some | apply [<dll>] | merged <dll>");
                return 2;
        }
    }

    private static int Merged(string dll) {
        using FileStream stream = File.OpenRead(dll);
        using PEReader image = new PEReader(stream);
        MetadataReader reader = image.GetMetadataReader();

        string[] banned = { "Concord.Generated", "Concord.Generators", "Microsoft.CodeAnalysis" };
        List<string> types = reader.TypeDefinitions
            .Select(reader.GetTypeDefinition)
            .Select(type => reader.GetString(type.Namespace) + "." + reader.GetString(type.Name))
            .ToList();
        List<string> found = types
            .Where(name => name != "Microsoft.CodeAnalysis.EmbeddedAttribute")
            .Where(name => banned.Any(ns => name.StartsWith(ns + ".", StringComparison.Ordinal)))
            .ToList();

        Console.WriteLine($"{dll}: {types.Count} types, {found.Count} from the generators");
        if (found.Count > 0) {
            Console.Error.WriteLine($"{dll}: expected no generator type, found [{string.Join(", ", found)}]");
            return 1;
        }

        return 0;
    }

    private static int Metadata(string dll, bool wantAttributes) {
        using FileStream stream = File.OpenRead(dll);
        using PEReader image = new PEReader(stream);
        MetadataReader reader = image.GetMetadataReader();

        int manifests = reader.TypeDefinitions
            .Select(reader.GetTypeDefinition)
            .Count(type => reader.GetString(type.Namespace) == "Concord.Generated" && reader.GetString(type.Name) == "PatchManifest");
        int attributes = reader.CustomAttributes
            .Count(handle => FromConcord(reader, reader.GetCustomAttribute(handle)));

        int registries = reader.GetAssemblyDefinition().GetCustomAttributes()
            .Count(handle => FromConcord(reader, reader.GetCustomAttribute(handle), "PatchRegistryAttribute"));
        List<string> contracts = reader.TypeDefinitions
            .Select(reader.GetTypeDefinition)
            .Where(type => reader.GetString(type.Namespace) == "Concord.Generated")
            .SelectMany(type => type.GetInterfaceImplementations().Select(handle => (Type: type, Interface: reader.GetInterfaceImplementation(handle).Interface)))
            .Where(pair => pair.Interface.Kind == HandleKind.TypeReference && FromConcord(reader, (TypeReferenceHandle)pair.Interface))
            .Select(pair => reader.GetString(pair.Type.Name))
            .ToList();

        Console.WriteLine($"{dll}: {manifests} manifest, {attributes} concord attributes, {registries} registry, {contracts.Count} generated types with a concord interface");
        if (manifests != 1) {
            Console.Error.WriteLine($"{dll}: expected one Concord.Generated.PatchManifest, found {manifests}");
            return 1;
        }

        if (wantAttributes != (attributes > 0)) {
            Console.Error.WriteLine($"{dll}: expected {(wantAttributes ? "concord attributes" : "no concord attribute")}, found {attributes}");
            return 1;
        }

        if (registries != (wantAttributes ? 1 : 0)) {
            Console.Error.WriteLine($"{dll}: expected {(wantAttributes ? "one" : "no")} [PatchRegistry] on the assembly, found {registries}");
            return 1;
        }

        if (!wantAttributes && contracts.Count > 0) {
            Console.Error.WriteLine($"{dll}: expected no generated type with a concord interface, found [{string.Join(", ", contracts)}]");
            return 1;
        }

        return 0;
    }

    private static bool FromConcord(MetadataReader reader, CustomAttribute attribute, string? name = null) {
        if (attribute.Constructor.Kind != HandleKind.MemberReference) {
            return false;
        }

        EntityHandle parent = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;
        return parent.Kind == HandleKind.TypeReference
            && FromConcord(reader, (TypeReferenceHandle)parent)
            && (name is null || reader.GetString(reader.GetTypeReference((TypeReferenceHandle)parent).Name) == name);
    }

    private static bool FromConcord(MetadataReader reader, TypeReferenceHandle type) {
        EntityHandle scope = reader.GetTypeReference(type).ResolutionScope;
        return scope.Kind == HandleKind.AssemblyReference
            && reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)scope).Name) == "Concord";
    }

    private static int Apply(Assembly mod) {
        StubBackend backend = new StubBackend();
        PatchBackend.Current = backend;
        MethodBase target = mod.GetTypes().Single(type => type.Name == nameof(ContractTarget)).GetMethod(nameof(ContractTarget.Method))!;
        using (Patcher.Apply(mod)) {
            if (!backend.Composed.Contains(target)) {
                Console.Error.WriteLine($"Patcher.Apply sent [{string.Join(", ", backend.Composed)}] to the backend, not {target}");
                return 1;
            }
        }

        Console.WriteLine($"Patcher.Apply sent {target} to the backend");
        return 0;
    }
}
#endif
