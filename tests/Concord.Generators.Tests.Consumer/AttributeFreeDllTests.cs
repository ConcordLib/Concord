using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace Concord.Generators.Tests.Consumer;

public sealed class AttributeFreeDllTests {
    private static readonly string Dll = typeof(SecretCounter).Assembly.Location;

    [Fact]
    public void Build_WithoutTheSymbol_LeavesNoConcordAttributeAnywhereInTheDll() {
        using FileStream stream = File.OpenRead(Dll);
        using PEReader image = new PEReader(stream);
        MetadataReader reader = image.GetMetadataReader();
        List<string> all = [];
        foreach (CustomAttributeHandle handle in reader.CustomAttributes) {
            all.Add(AttributeType(reader, reader.GetCustomAttribute(handle)));
        }

        Assert.NotEmpty(all);
        Assert.DoesNotContain(all, type => type.StartsWith("[Concord", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_WithoutTheSymbol_WritesAManifestAHostCanLoadWithoutConcord() {
        using FileStream stream = File.OpenRead(Dll);
        using PEReader image = new PEReader(stream);
        MetadataReader reader = image.GetMetadataReader();
        TypeDefinition manifest = reader.TypeDefinitions
            .Select(reader.GetTypeDefinition)
            .Single(type => reader.GetString(type.Namespace) == "Concord.Generated" && reader.GetString(type.Name) == "PatchManifest");

        Assert.Equal(TypeAttributes.Abstract | TypeAttributes.Sealed, manifest.Attributes & (TypeAttributes.Abstract | TypeAttributes.Sealed));
        Assert.Equal("[System.Runtime]System.Object", TypeName(reader, manifest.BaseType));
        Assert.Empty(manifest.GetInterfaceImplementations());
        Assert.Empty(manifest.GetFields());
        Assert.Empty(manifest.GetProperties());
        Assert.Empty(manifest.GetEvents());
        Assert.Empty(manifest.GetNestedTypes());
        Assert.Empty(manifest.GetCustomAttributes());

        List<string> publicMethods = [];
        foreach (MethodDefinitionHandle handle in manifest.GetMethods()) {
            MethodDefinition method = reader.GetMethodDefinition(handle);
            MethodSignature<string> signature = method.DecodeSignature(new SignatureNames(reader), null);
            Assert.True(method.Attributes.HasFlag(MethodAttributes.Static));
            Assert.Empty(method.GetCustomAttributes());
            Assert.All(method.GetParameters(), parameter => Assert.Empty(reader.GetParameter(parameter).GetCustomAttributes()));
            Assert.DoesNotContain(signature.ParameterTypes.Add(signature.ReturnType), type => type.Contains("[Concord", StringComparison.Ordinal));
            if ((method.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public) {
                publicMethods.Add(reader.GetString(method.Name) + "(" + string.Join(",", signature.ParameterTypes) + ")" + signature.ReturnType);
            }
        }

        Assert.Equal(["Describe(object)void"], publicMethods);
    }

    private static string AttributeType(MetadataReader reader, CustomAttribute attribute) {
        EntityHandle type = attribute.Constructor.Kind == HandleKind.MemberReference
            ? reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent
            : reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType();
        return TypeName(reader, type);
    }

    private static string TypeName(MetadataReader reader, EntityHandle handle) {
        switch (handle.Kind) {
            case HandleKind.TypeDefinition:
                TypeDefinition definition = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
                return "[this]" + reader.GetString(definition.Namespace) + "." + reader.GetString(definition.Name);
            case HandleKind.TypeReference:
                TypeReference reference = reader.GetTypeReference((TypeReferenceHandle)handle);
                string scope = reference.ResolutionScope.Kind switch {
                    HandleKind.AssemblyReference => "[" + reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)reference.ResolutionScope).Name) + "]",
                    HandleKind.TypeReference => TypeName(reader, reference.ResolutionScope) + "+",
                    _ => "[this]",
                };
                return scope + reader.GetString(reference.Namespace) + "." + reader.GetString(reference.Name);
            default:
                return handle.Kind.ToString();
        }
    }

    private sealed class SignatureNames(MetadataReader reader) : ISignatureTypeProvider<string, object?> {
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) {
            return typeCode.ToString().ToLowerInvariant();
        }

        public string GetTypeFromDefinition(MetadataReader metadata, TypeDefinitionHandle handle, byte rawTypeKind) {
            return TypeName(reader, handle);
        }

        public string GetTypeFromReference(MetadataReader metadata, TypeReferenceHandle handle, byte rawTypeKind) {
            return TypeName(reader, handle);
        }

        public string GetTypeFromSpecification(MetadataReader metadata, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) {
            return reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
        }

        public string GetSZArrayType(string elementType) {
            return elementType + "[]";
        }

        public string GetArrayType(string elementType, ArrayShape shape) {
            return elementType + "[" + new string(',', shape.Rank - 1) + "]";
        }

        public string GetByReferenceType(string elementType) {
            return elementType + "&";
        }

        public string GetPointerType(string elementType) {
            return elementType + "*";
        }

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) {
            return genericType + "<" + string.Join(",", typeArguments) + ">";
        }

        public string GetGenericTypeParameter(object? genericContext, int index) {
            return "!" + index;
        }

        public string GetGenericMethodParameter(object? genericContext, int index) {
            return "!!" + index;
        }

        public string GetFunctionPointerType(MethodSignature<string> signature) {
            return "method " + signature.ReturnType + "(" + string.Join(",", signature.ParameterTypes) + ")";
        }

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) {
            return unmodifiedType + " modopt(" + modifier + ")";
        }

        public string GetPinnedType(string elementType) {
            return elementType + " pinned";
        }
    }
}
