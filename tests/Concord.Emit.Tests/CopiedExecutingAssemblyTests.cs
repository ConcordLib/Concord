using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using Xunit;
using MethodBody = Mono.Cecil.Cil.MethodBody;

namespace Concord.Emit.Tests;

public sealed class CopiedExecutingAssemblyTests {
    private const int DeclaredOffset = 42;

    [Fact]
    public void TheInstructionsSynthesizedForAGetExecutingAssemblyCallKeepTheCallsDeclaredOffset() {
        MethodInfo subject = typeof(CopiedExecutingAssemblyTests)
            .GetMethod(nameof(Reports), BindingFlags.NonPublic | BindingFlags.Static)!;
        using DynamicMethodDefinition source = new DynamicMethodDefinition(subject);
        using DynamicMethodDefinition destination = new DynamicMethodDefinition(subject);

        MethodBody body = source.Definition.Body;
        Instruction call = body.Instructions.Single(IsExecutingAssemblyCall);

        List<Instruction> synthesized;
        List<int> origins;
        InstructionOrigins.Enabled = true;
        try {
            InstructionOrigins.Record(call, DeclaredOffset);
            BodyCopier.CopySpine(source.Definition, destination.Definition, typeof(CopiedExecutingAssemblyTests));

            synthesized = destination.Definition.Body.Instructions
                .Where(step => step.OpCode == OpCodes.Call || step.OpCode == OpCodes.Callvirt)
                .ToList();
            origins = synthesized.ConvertAll(InstructionOrigins.OffsetOf);
        } finally {
            InstructionOrigins.Enabled = false;
        }

        Assert.True(
            synthesized.Count == 2 && origins.TrueForAll(origin => origin == DeclaredOffset),
            $"the copy synthesized {synthesized.Count} instructions for the GetExecutingAssembly call and gave " +
            $"them origins [{string.Join(", ", origins)}] instead of [{DeclaredOffset}, {DeclaredOffset}]. The " +
            "call carried a recorded origin, so the pair standing in for it has to carry the same one or a stack " +
            "trace through the copy collapses those frames onto the wrong source line.");
    }

    private static bool IsExecutingAssemblyCall(Instruction step) {
        return step.OpCode == OpCodes.Call
            && step.Operand is MethodReference method
            && method.Name == nameof(Assembly.GetExecutingAssembly);
    }

    private static Assembly Reports() {
        return Assembly.GetExecutingAssembly();
    }
}
