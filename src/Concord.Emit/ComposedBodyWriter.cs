using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MethodBody = Mono.Cecil.Cil.MethodBody;

namespace Concord.Emit;

internal static class ComposedBodyWriter {
    internal const string RefusalCode = "CONC177";

    internal static void Write(MethodDefinition destination, ComposedBody body, MethodBase original) {
        ModuleDefinition module = destination.Module;
        MethodBody target = body is null ? throw new ArgumentNullException(nameof(body)) : destination.Body;

        target.Instructions.Clear();
        target.Variables.Clear();
        target.ExceptionHandlers.Clear();
        target.InitLocals = body.InitLocals;

        foreach (ComposedLocal local in body.Locals) {
            TypeReference imported = module.ImportReference(local.Type);
            target.Variables.Add(new VariableDefinition(local.IsPinned ? new PinnedType(imported) : imported));
        }

        List<CodeInstruction> instructions = new List<CodeInstruction>(body.Instructions.Count);
        foreach (ComposedInstruction step in body.Instructions) {
            if (step.OriginOffset is { } origin) {
                InstructionOrigins.Record(step.Instruction, origin);
            }

            instructions.Add(step.Instruction);
        }

        TranspilerContext context = new TranspilerContext(original);
        context.ExistingLocalCount = target.Variables.Count;
        foreach (ComposedLocal local in body.Locals) {
            context.ExistingLocalTypes.Add(local.Type);
        }

        CecilCodeConverter.WriteBack(destination, instructions, context);
        StampOffsets(destination.Body, body);
        Attach(destination, body, module, original);
    }

    private static void StampOffsets(MethodBody target, ComposedBody body) {
        int count = Math.Min(target.Instructions.Count, body.Instructions.Count);
        for (int i = 0; i < count; i++) {
            if (body.Instructions[i].OriginOffset is { } offset) {
                target.Instructions[i].Offset = offset;
            }
        }
    }

    private static void Attach(MethodDefinition destination, ComposedBody body, ModuleDefinition module, MethodBase original) {
        MethodBody target = destination.Body;
        List<Instruction> emitted = new List<Instruction>(target.Instructions);
        target.ExceptionHandlers.Clear();

        foreach (ComposedClause clause in body.Clauses) {
            ExceptionHandler handler = new ExceptionHandler(Kind(clause.Kind)) {
                TryStart = At(emitted, clause.TryStart, original, "try start"),
                TryEnd = At(emitted, clause.TryEnd, original, "try end"),
                HandlerStart = At(emitted, clause.HandlerStart, original, "handler start"),
                HandlerEnd = clause.HandlerEnd >= emitted.Count ? null : At(emitted, clause.HandlerEnd, original, "handler end"),
            };

            if (clause.Kind == ClauseKind.Catch) {
                handler.CatchType = module.ImportReference(
                    clause.CatchType ?? throw Refuse(original, "a catch clause carries no caught type"));
            }

            if (clause.Kind == ClauseKind.Filter) {
                handler.FilterStart = At(emitted, clause.FilterStart, original, "filter start");
            }

            target.ExceptionHandlers.Add(handler);
        }
    }

    private static Instruction At(List<Instruction> emitted, int index, MethodBase original, string role) {
        if (index < 0 || index >= emitted.Count) {
            throw Refuse(original, $"its {role} is instruction {index} of {emitted.Count}");
        }

        return emitted[index];
    }

    private static ExceptionHandlerType Kind(ClauseKind kind) {
        return kind switch {
            ClauseKind.Catch => ExceptionHandlerType.Catch,
            ClauseKind.Filter => ExceptionHandlerType.Filter,
            ClauseKind.Finally => ExceptionHandlerType.Finally,
            _ => ExceptionHandlerType.Fault,
        };
    }

    private static ConcordEmitException Refuse(MethodBase original, string because) {
        return new ConcordEmitException(
            RefusalCode,
            $"'{original.DeclaringType?.FullName ?? "<no declaring type>"}.{original.Name}' decoded to a body " +
            $"Cecil cannot carry: {because}.");
    }
}
