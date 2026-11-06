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
        using MonoMod.Utils.DynamicMethodDefinition definition =
            new MonoMod.Utils.DynamicMethodDefinition(name, returnType, parameterTypes);
        ComposedBodyWriter.Write(definition.Definition, body, origin);
        return NeedsCecilGenerator(body)
            ? MonoMod.Utils.DMDGenerator<MonoMod.Utils.DMDCecilGenerator>.Generate(definition, null)
            : definition.Generate();
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

#if !NET
    private static bool NeedsCecilGenerator(ComposedBody body) {
        HashSet<(int Start, int End)> tries = new HashSet<(int Start, int End)>();
        foreach (ComposedClause clause in body.Clauses) {
            if (!tries.Add((clause.TryStart, clause.TryEnd))) {
                return true;
            }
        }

        foreach (ComposedInstruction instruction in body.Instructions) {
            if (instruction.Instruction.operand is CallSiteSignature) {
                return true;
            }
        }

        return false;
    }
#endif

    private static byte[] DynamicScopeLocals(ComposedBody body) {
        SignatureHelper helper = SignatureHelper.GetLocalVarSigHelper();
        foreach (ComposedLocal local in body.Locals) {
            helper.AddArgument(local.Type, local.IsPinned);
        }

        return helper.GetSignature();
    }
}
