using System.Reflection;
using OpCode = System.Reflection.Emit.OpCode;
using OperandType = System.Reflection.Emit.OperandType;

namespace Concord.Emit;

internal static class ScratchBodyWriter {
    internal static MethodInfo Write(ComposedBody body, IScratchBody sink) {
        using (sink) {
            sink.InitLocals(body.InitLocals);
            foreach (ComposedLocal local in body.Locals) {
                sink.DeclareLocal(local.Type, local.IsPinned);
            }

            foreach (ComposedInstruction step in body.Instructions) {
                if (step.OriginOffset is { } origin) {
                    InstructionOrigins.Record(step.Instruction, origin);
                }

                foreach (Label label in step.Instruction.labels) {
                    sink.MarkLabel(label.Id);
                }

                Emit(sink, step.Instruction);
            }

            foreach (ComposedClause clause in body.Clauses) {
                sink.AddClause(
                    clause.Kind,
                    clause.TryStart,
                    clause.TryEnd,
                    clause.HandlerStart,
                    clause.HandlerEnd,
                    clause.CatchType,
                    clause.FilterStart);
            }

            return sink.Finish();
        }
    }

    private static void Emit(IScratchBody sink, CodeInstruction instruction) {
        OpCode opcode = instruction.opcode;
        if (opcode.OperandType == OperandType.InlineNone) {
            sink.Emit(opcode);
            return;
        }

        switch (instruction.operand) {
            case null:
                sink.Emit(opcode);
                break;
            case Label label:
                sink.EmitBranch(opcode, label.Id);
                break;
            case Label[] labels:
                sink.EmitSwitch(Ids(labels));
                break;
            case LocalRef local:
                sink.Emit(opcode, local.Index);
                break;
            case CallSiteSignature site:
                sink.EmitCalli(opcode, site.ReturnType, site.Parameters, (int)site.Convention, site.HasThis, site.ExplicitThis);
                break;
            case Type type:
                sink.Emit(opcode, type);
                break;
            case FieldInfo field:
                sink.Emit(opcode, field);
                break;
            case MethodBase method:
                sink.Emit(opcode, method);
                break;
            case string text:
                sink.Emit(opcode, text);
                break;
            case sbyte i8:
                sink.Emit(opcode, i8);
                break;
            case byte u8:
                sink.Emit(opcode, u8);
                break;
            case short i16:
                sink.Emit(opcode, i16);
                break;
            case ushort u16:
                sink.Emit(opcode, u16);
                break;
            case int i32:
                sink.Emit(opcode, i32);
                break;
            case long i64:
                sink.Emit(opcode, i64);
                break;
            case float f32:
                sink.Emit(opcode, f32);
                break;
            case double f64:
                sink.Emit(opcode, f64);
                break;
            default:
                throw new ConcordEmitException(
                    CecilCodeConverter.CodeConversionFailed,
                    $"Unsupported operand '{instruction.operand}' of type '{instruction.operand.GetType()}' for opcode " +
                    $"'{opcode.Name}'. Supported operands are MethodBase, FieldInfo, Type, Label, LocalRef, string, and " +
                    "int/long/float/double literals.");
        }
    }

    private static int[] Ids(Label[] labels) {
        int[] ids = new int[labels.Length];
        for (int i = 0; i < ids.Length; i++) {
            ids[i] = labels[i].Id;
        }

        return ids;
    }
}
