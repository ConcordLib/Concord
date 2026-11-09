using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using DynamicILInfo = System.Reflection.Emit.DynamicILInfo;
using DynamicMethod = System.Reflection.Emit.DynamicMethod;
using SignatureHelper = System.Reflection.Emit.SignatureHelper;

namespace Concord.Emit;

internal sealed class ScratchMethod : IDisposable {
    private static readonly RecordingImporters Importers = new RecordingImporters();

    private readonly ModuleDefinition module;

    private readonly MethodBase origin;

    private readonly Type returnType;

    private readonly Type[] parameterTypes;

    internal ScratchMethod(string name, Type returnType, Type[] parameterTypes, MethodBase origin) {
        this.returnType = returnType;
        this.parameterTypes = parameterTypes;
        this.origin = origin;

        module = ModuleDefinition.CreateModule(
            "Concord.Scratch",
            new ModuleParameters {
                Kind = ModuleKind.Dll,
                ReflectionImporterProvider = Importers,
                MetadataImporterProvider = Importers,
            });

        TypeDefinition holder = new TypeDefinition(
            string.Empty,
            "<Scratch>",
            Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed,
            module.TypeSystem.Object);
        module.Types.Add(holder);

        Definition = new MethodDefinition(
            name,
            Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static | Mono.Cecil.MethodAttributes.HideBySig,
            module.ImportReference(returnType));
        foreach (Type parameter in parameterTypes) {
            Definition.Parameters.Add(new ParameterDefinition(module.ImportReference(parameter)));
        }

        holder.Methods.Add(Definition);
    }

    internal MethodDefinition Definition { get; }

    internal ModuleDefinition Module => module;

    public void Dispose() {
        module.Dispose();
    }

    internal static ScratchMethod From(MethodBase method, ComposedBody body, string? name = null) {
        ScratchMethod scratch = new ScratchMethod(
            name ?? method.Name,
            WrapperComposer.ResolveReturnType(method),
            WrapperComposer.ResolveParameterTypes(method),
            method);
        try {
            ComposedBodyWriter.Write(scratch.Definition, body, method);
            return scratch;
        } catch {
            scratch.Dispose();
            throw;
        }
    }

    internal static MethodInfo Generate(
        ComposedBody body,
        string name,
        Type returnType,
        Type[] parameterTypes,
        MethodBase origin) {
#if NET
        DynamicMethod method = new DynamicMethod(name, returnType, parameterTypes, true);

        DynamicILInfo scope = method.GetDynamicILInfo();
        EncodedBody encoded = BodyEncoder.Encode(body, new DynamicScopeTokens(scope), new ComputedStack());
        scope.SetLocalSignature(DynamicScopeLocals(body));
        scope.SetCode(encoded.Il.ToArray(), encoded.MaxStack);
        if (!encoded.ExceptionSection.IsEmpty) {
            scope.SetExceptions(encoded.ExceptionSection.ToArray());
        }

        return method;
#else
        IScratchGenerator generator = ScratchGenerator.Current
            ?? throw new ConcordEmitException(
                "CONC183",
                "No scratch generator is installed, so there is no way to emit a method body on this " +
                "runtime. Concord can compose a body here but cannot turn it into a callable method. " +
                "The mono backend installs one.");

        return ScratchBodyWriter.Write(body, generator.Begin(name, returnType, parameterTypes, origin));
#endif
    }

    internal ILProcessor GetILProcessor() {
        return Definition.Body.GetILProcessor();
    }

    internal MethodInfo Generate() {
        return Generate(
            ComposedBodyFactory.Create(Definition, new TranspilerContext(origin)),
            Definition.Name,
            returnType,
            parameterTypes,
            origin);
    }

    private static byte[] DynamicScopeLocals(ComposedBody body) {
        SignatureHelper helper = SignatureHelper.GetLocalVarSigHelper();
        foreach (ComposedLocal local in body.Locals) {
            helper.AddArgument(local.Type, local.IsPinned);
        }

        return helper.GetSignature();
    }
}
