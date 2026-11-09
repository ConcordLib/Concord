using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using ExceptionHandler = Mono.Cecil.Cil.ExceptionHandler;
using MethodBody = Mono.Cecil.Cil.MethodBody;
using OpCode = System.Reflection.Emit.OpCode;
using OpCodes = System.Reflection.Emit.OpCodes;
using OperandType = Mono.Cecil.Cil.OperandType;

namespace Concord.Emit;

internal static partial class CecilCodeConverter {
    // Every failure to map a Cecil body onto the Concord model reports the same code; the
    // message carries the specific reason.
    internal const string CodeConversionFailed = "CONC118";

    private static readonly Dictionary<string, Mono.Cecil.Cil.OpCode> CecilOpCodesByName = BuildCecilOpCodeTable();

    // Returns the caller-known label ids each newly created instruction carries, keyed by the fresh
    // Instruction object written into the body - the provenance a subsequent ToInstructions read can
    // use to hand a caller back a Label with the same Id it supplied, instead of a fresh one. Empty
    // for any instruction the caller did not attach a label to.
    public static Dictionary<Instruction, List<int>> WriteBack(MethodDefinition definition, IReadOnlyList<CodeInstruction> instructions, TranspilerContext context) {
        ModuleDefinition module = definition.Module;
        MethodBody body = definition.Body;

        foreach (Type declaredLocal in context.DeclaredLocals) {
            body.Variables.Add(new VariableDefinition(module.ImportReference(declaredLocal)));
        }

        Dictionary<int, VariableDefinition> localsByIndex = new Dictionary<int, VariableDefinition>();
        foreach (VariableDefinition variable in body.Variables) {
            localsByIndex[variable.Index] = variable;
        }

        List<Instruction> emitted = new List<Instruction>(instructions.Count);
        foreach (CodeInstruction instruction in instructions) {
            Instruction created = CreateInstruction(instruction, definition, localsByIndex, module);
            InstructionOrigins.CarryOver(instruction, created);
            emitted.Add(created);
        }

        Dictionary<Label, Instruction> instructionByLabel = BuildLabelMap(instructions, emitted);
        for (int i = 0; i < instructions.Count; i++) {
            FixupOperand(emitted[i], instructions[i], instructionByLabel);
        }

        ApplyBlocks(definition, emitted, instructions, context);

        try {
            body.Instructions.Clear();
            ILProcessor il = body.GetILProcessor();
            foreach (Instruction instruction in emitted) {
                il.Append(instruction);
            }
        } catch (ConcordEmitException) {
            throw;
        } catch (Exception ex) {
            throw new ConcordEmitException("CONC119", $"Write-back produced a method body Cecil rejected: {ex.Message}");
        }

        return BuildLabelProvenance(instructions, emitted);
    }

    private static Dictionary<Instruction, List<int>> BuildLabelProvenance(IReadOnlyList<CodeInstruction> instructions, List<Instruction> emitted) {
        Dictionary<Instruction, List<int>> provenance = new Dictionary<Instruction, List<int>>();
        for (int i = 0; i < instructions.Count; i++) {
            if (instructions[i].labels.Count == 0) {
                continue;
            }

            List<int> ids = new List<int>(instructions[i].labels.Count);
            foreach (Label label in instructions[i].labels) {
                ids.Add(label.Id);
            }

            provenance[emitted[i]] = ids;
        }

        return provenance;
    }

    private static Dictionary<string, Mono.Cecil.Cil.OpCode> BuildCecilOpCodeTable() {
        Dictionary<string, Mono.Cecil.Cil.OpCode> table = new Dictionary<string, Mono.Cecil.Cil.OpCode>(StringComparer.Ordinal);
        FieldInfo[] fields = typeof(Mono.Cecil.Cil.OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static);
        foreach (FieldInfo field in fields) {
            if (field.FieldType != typeof(Mono.Cecil.Cil.OpCode)) {
                continue;
            }

            Mono.Cecil.Cil.OpCode opcode = (Mono.Cecil.Cil.OpCode)field.GetValue(null)!;
            if (opcode.Name is not null) {
                table[opcode.Name] = opcode;
            }
        }

        table["ldelem"] = Mono.Cecil.Cil.OpCodes.Ldelem_Any;
        table["stelem"] = Mono.Cecil.Cil.OpCodes.Stelem_Any;

        return table;
    }

    private static Mono.Cecil.Cil.OpCode MapOpCodeToCecil(OpCode source) {
        if (source.Name is not null && CecilOpCodesByName.TryGetValue(source.Name, out Mono.Cecil.Cil.OpCode mapped)) {
            return mapped;
        }

        throw new ConcordEmitException(CodeConversionFailed, $"Unknown opcode '{source.Name}'. Use an OpCodes.* value from System.Reflection.Emit.");
    }

    private static Dictionary<Label, Instruction> BuildLabelMap(IReadOnlyList<CodeInstruction> instructions, List<Instruction> emitted) {
        Dictionary<Label, Instruction> map = new Dictionary<Label, Instruction>();
        for (int i = 0; i < instructions.Count; i++) {
            foreach (Label label in instructions[i].labels) {
                map[label] = emitted[i];
            }
        }

        return map;
    }

    private static Instruction CreateInstruction(CodeInstruction source, MethodDefinition definition, Dictionary<int, VariableDefinition> localsByIndex, ModuleDefinition module) {
        try {
            return BuildInstruction(source, definition, localsByIndex, module);
        } catch (ArgumentException) {
            throw new ConcordEmitException(
                CodeConversionFailed,
                $"Opcode '{source.opcode.Name}' expects a {MapOpCodeToCecil(source.opcode).OperandType} operand, but the transpiler gave it " +
                $"'{source.operand ?? "null"}' of type '{source.operand?.GetType().Name ?? "null"}', which Cecil refused.");
        }
    }

    private static Instruction BuildInstruction(CodeInstruction source, MethodDefinition definition, Dictionary<int, VariableDefinition> localsByIndex, ModuleDefinition module) {
        Mono.Cecil.Cil.OpCode opcode = MapOpCodeToCecil(source.opcode);
        if (opcode.OperandType == OperandType.InlineNone) {
            return Instruction.Create(opcode);
        }

        return source.operand switch {
            null => Instruction.Create(opcode),
            Label => Instruction.Create(opcode, Instruction.Create(Mono.Cecil.Cil.OpCodes.Nop)),
            Label[] => Instruction.Create(opcode, Array.Empty<Instruction>()),
            LocalRef local => Instruction.Create(opcode, ResolveLocal(local, localsByIndex, source)),
            CallSiteSignature callSite => Instruction.Create(opcode, BuildCallSite(callSite, module)),
            Type type => Instruction.Create(opcode, module.ImportReference(type)),
            FieldInfo field => Instruction.Create(opcode, module.ImportReference(field)),
            ConstructorInfo ctor => Instruction.Create(opcode, module.ImportReference(ctor)),
            MethodInfo method => Instruction.Create(opcode, module.ImportReference(method)),
            string s => Instruction.Create(opcode, s),
            sbyte sb => ResolveSlotOperand(opcode, sb, definition, localsByIndex, source) ?? CreateIntegerInstruction(opcode, sb, source),
            byte b => ResolveSlotOperand(opcode, b, definition, localsByIndex, source) ?? CreateIntegerInstruction(opcode, b, source),
            short i16 => ResolveSlotOperand(opcode, i16, definition, localsByIndex, source) ?? CreateIntegerInstruction(opcode, i16, source),
            ushort u16 => ResolveSlotOperand(opcode, u16, definition, localsByIndex, source) ?? CreateIntegerInstruction(opcode, u16, source),
            int i32 => ResolveSlotOperand(opcode, i32, definition, localsByIndex, source) ?? CreateIntegerInstruction(opcode, i32, source),
            long i64 => CreateIntegerInstruction(opcode, i64, source),
            float f32 => CreateRealInstruction(opcode, f32, source),
            double f64 => CreateRealInstruction(opcode, f64, source),
            _ => throw new ConcordEmitException(CodeConversionFailed, $"Unsupported operand '{source.operand}' of type '{source.operand.GetType()}' for opcode '{source.opcode.Name}'. Supported operands are MethodBase, FieldInfo, Type, Label, LocalBuilder, string, and int/long/float/double literals."),
        };
    }

    private static CallSite BuildCallSite(CallSiteSignature signature, ModuleDefinition module) {
        CallSite site = new CallSite(module.ImportReference(signature.ReturnType)) {
            CallingConvention = (MethodCallingConvention)(int)signature.Convention,
            HasThis = signature.HasThis,
            ExplicitThis = signature.ExplicitThis,
        };

        foreach (Type parameter in signature.Parameters) {
            site.Parameters.Add(new ParameterDefinition(module.ImportReference(parameter)));
        }

        return site;
    }

    private static Instruction CreateIntegerInstruction(Mono.Cecil.Cil.OpCode opcode, long value, CodeInstruction source) {
        switch (opcode.OperandType) {
            case OperandType.ShortInlineI:
                return opcode.Code == Code.Ldc_I4_S
                    ? Instruction.Create(opcode, checked((sbyte)value))
                    : Instruction.Create(opcode, checked((byte)value));
            case OperandType.InlineI:
                return Instruction.Create(opcode, checked((int)value));
            case OperandType.InlineI8:
                return Instruction.Create(opcode, value);
            case OperandType.ShortInlineR:
                return Instruction.Create(opcode, (float)value);
            case OperandType.InlineR:
                return Instruction.Create(opcode, (double)value);
            default:
                throw new ConcordEmitException(
                    CodeConversionFailed,
                    $"Opcode '{source.opcode.Name}' does not take an integer operand, but the transpiler gave it '{value}'.");
        }
    }

    private static Instruction CreateRealInstruction(Mono.Cecil.Cil.OpCode opcode, double value, CodeInstruction source) {
        return opcode.OperandType switch {
            OperandType.ShortInlineR => Instruction.Create(opcode, (float)value),
            OperandType.InlineR => Instruction.Create(opcode, value),
            _ => throw new ConcordEmitException(CodeConversionFailed, $"Opcode '{source.opcode.Name}' does not take a floating-point operand, but the transpiler gave it '{value}' of type '{source.operand?.GetType()}'."),
        };
    }

    private static bool IsArgumentOperand(Mono.Cecil.Cil.OpCode opcode) {
        return opcode.OperandType == OperandType.ShortInlineArg || opcode.OperandType == OperandType.InlineArg;
    }

    private static bool IsLocalOperand(Mono.Cecil.Cil.OpCode opcode) {
        return opcode.OperandType == OperandType.ShortInlineVar || opcode.OperandType == OperandType.InlineVar;
    }

    // A bare slot number is how Harmony transpilers address both arguments and locals, so an int
    // operand resolves against whichever table the opcode calls for rather than being emitted raw.
    private static Instruction? ResolveSlotOperand(
        Mono.Cecil.Cil.OpCode opcode,
        int rawSlot,
        MethodDefinition definition,
        Dictionary<int, VariableDefinition> localsByIndex,
        CodeInstruction source) {
        if (IsArgumentOperand(opcode)) {
            return Instruction.Create(opcode, ResolveParameter(definition, rawSlot));
        }

        if (IsLocalOperand(opcode)) {
            return Instruction.Create(opcode, ResolveLocalBySlot(rawSlot, localsByIndex, source));
        }

        return null;
    }

    private static VariableDefinition ResolveLocalBySlot(int rawSlot, Dictionary<int, VariableDefinition> localsByIndex, CodeInstruction source) {
        if (localsByIndex.TryGetValue(rawSlot, out VariableDefinition? variable)) {
            return variable;
        }

        throw new ConcordEmitException(CodeConversionFailed, $"Local slot {rawSlot} referenced by opcode '{source.opcode.Name}' is out of range for a body with {localsByIndex.Count} local(s).");
    }

    private static ParameterDefinition ResolveParameter(MethodDefinition definition, int rawSlot) {
        if (rawSlot < 0 || rawSlot >= definition.Parameters.Count) {
            throw new ConcordEmitException(CodeConversionFailed, $"Argument slot {rawSlot} is out of range for a method with {definition.Parameters.Count} parameter(s).");
        }

        return definition.Parameters[rawSlot];
    }

    private static VariableDefinition ResolveLocal(LocalRef local, Dictionary<int, VariableDefinition> localsByIndex, CodeInstruction source) {
        if (localsByIndex.TryGetValue(local.Index, out VariableDefinition? variable)) {
            return variable;
        }

        throw new ConcordEmitException(CodeConversionFailed, $"Local '{local}' referenced by opcode '{source.opcode.Name}' has no matching variable.");
    }

    private static void FixupOperand(Instruction emitted, CodeInstruction source, Dictionary<Label, Instruction> instructionByLabel) {
        if (source.operand is Label label) {
            emitted.Operand = ResolveLabelTarget(label, instructionByLabel, source);
        } else if (source.operand is Label[] labels) {
            Instruction[] targets = new Instruction[labels.Length];
            for (int i = 0; i < labels.Length; i++) {
                targets[i] = ResolveLabelTarget(labels[i], instructionByLabel, source);
            }

            emitted.Operand = targets;
        }
    }

    private static Instruction ResolveLabelTarget(Label label, Dictionary<Label, Instruction> map, CodeInstruction source) {
        if (map.TryGetValue(label, out Instruction? target)) {
            return target;
        }

        throw new ConcordEmitException(CodeConversionFailed, $"Label '{label}' referenced by opcode '{source.opcode.Name}' is not attached to any instruction.");
    }

    private static void ApplyBlocks(MethodDefinition definition, List<Instruction> emitted, IReadOnlyList<CodeInstruction> instructions, TranspilerContext context) {
        List<ExceptionFrame> open = new List<ExceptionFrame>();
        definition.Body.ExceptionHandlers.Clear();

        for (int i = 0; i < instructions.Count; i++) {
            foreach (ExceptionBlock block in instructions[i].blocks) {
                ApplyBlock(definition, emitted[i], block, open, i, context);
            }
        }

        if (open.Count > 0) {
            throw new ConcordEmitException(CodeConversionFailed, $"{open.Count} exception block(s) were opened and never closed.");
        }
    }

    private static void ApplyBlock(MethodDefinition definition, Instruction position, ExceptionBlock block, List<ExceptionFrame> open, int index, TranspilerContext context) {
        if (block.blockType == ExceptionBlockType.BeginExceptionBlock) {
            open.Add(new ExceptionFrame(position));
            return;
        }

        if (open.Count == 0) {
            throw new ConcordEmitException(CodeConversionFailed, $"Exception block '{block.blockType}' at instruction {index} closes a region that was never opened.");
        }

        ExceptionFrame frame = open[open.Count - 1];

        if (block.blockType == ExceptionBlockType.EndExceptionBlock) {
            if (frame.TryEndPosition is null) {
                throw new ConcordEmitException(CodeConversionFailed, $"Exception block at instruction {index} closes a try region that opened no handlers.");
            }

            // HandlerEnd is exclusive in Cecil - there is no instruction one past the end of the
            // body, so a handler that legitimately runs to end-of-body was recorded in
            // context.EndOfBodyBlocks (by the same ExceptionBlock object reference) when this stream
            // was produced by ToInstructions. Restore null rather than the carrier instruction the
            // marker happened to be attached to, or the handler is silently truncated by one
            // instruction.
            Instruction? handlerEnd = context.EndOfBodyBlocks.Contains(block) ? null : position;
            CloseCurrentHandler(definition, frame, handlerEnd, index);
            open.RemoveAt(open.Count - 1);
            return;
        }

        if (block.blockType == ExceptionBlockType.BeginExceptFilterBlock) {
            CloseCurrentHandler(definition, frame, position, index);
            ExceptionHandler handler = new ExceptionHandler(ExceptionHandlerType.Filter) {
                TryStart = frame.TryStart,
                FilterStart = position,
            };
            frame.TryEndPosition ??= position;
            handler.TryEnd = frame.TryEndPosition;
            frame.Current = handler;
            frame.CurrentAwaitsHandlerStart = true;
            return;
        }

        if (block.blockType == ExceptionBlockType.BeginCatchBlock && frame.CurrentAwaitsHandlerStart) {
            frame.Current!.HandlerStart = position;
            frame.CurrentAwaitsHandlerStart = false;
            return;
        }

        CloseCurrentHandler(definition, frame, position, index);

        ExceptionHandlerType handlerType = block.blockType switch {
            ExceptionBlockType.BeginCatchBlock => ExceptionHandlerType.Catch,
            ExceptionBlockType.BeginFaultBlock => ExceptionHandlerType.Fault,
            ExceptionBlockType.BeginFinallyBlock => ExceptionHandlerType.Finally,
            _ => throw new ConcordEmitException(CodeConversionFailed, $"Unexpected exception block '{block.blockType}' at instruction {index}."),
        };

        ExceptionHandler newHandler = new ExceptionHandler(handlerType) {
            TryStart = frame.TryStart,
            HandlerStart = position,
        };
        frame.TryEndPosition ??= position;
        newHandler.TryEnd = frame.TryEndPosition;

        if (handlerType == ExceptionHandlerType.Catch && block.catchType is not null) {
            newHandler.CatchType = definition.Module.ImportReference(block.catchType);
        }

        frame.Current = newHandler;
        frame.CurrentAwaitsHandlerStart = false;
    }

    private static void CloseCurrentHandler(MethodDefinition definition, ExceptionFrame frame, Instruction? endPosition, int index) {
        if (frame.Current is null) {
            return;
        }

        if (frame.CurrentAwaitsHandlerStart) {
            throw new ConcordEmitException(CodeConversionFailed, $"Filter block at instruction {index} was never followed by its BeginCatchBlock.");
        }

        frame.Current.HandlerEnd = endPosition;
        definition.Body.ExceptionHandlers.Add(frame.Current);
        frame.Current = null;
    }

    private sealed class ExceptionFrame {
        internal ExceptionFrame(Instruction tryStart) {
            TryStart = tryStart;
        }

        internal Instruction TryStart { get; }

        internal Instruction? TryEndPosition { get; set; }

        internal ExceptionHandler? Current { get; set; }

        internal bool CurrentAwaitsHandlerStart { get; set; }
    }
}
