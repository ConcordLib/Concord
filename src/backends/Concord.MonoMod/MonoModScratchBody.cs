using System.Reflection;
using Concord.Emit;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using MethodBody = Mono.Cecil.Cil.MethodBody;
using OpCode = System.Reflection.Emit.OpCode;
using OperandType = Mono.Cecil.Cil.OperandType;

namespace Concord.MonoMod;

internal sealed class MonoModScratchBody : IScratchBody {
    private const string RefusalCode = "CONC118";

    private static readonly Dictionary<string, Mono.Cecil.Cil.OpCode> CecilOpCodesByName = BuildCecilOpCodeTable();

    private readonly DynamicMethodDefinition definition;

    private readonly MethodBase origin;

    private readonly MethodDefinition method;

    private readonly ModuleDefinition module;

    private readonly MethodBody body;

    private readonly ILProcessor il;

    private readonly Dictionary<int, int> labelPositions = new Dictionary<int, int>();

    private readonly List<KeyValuePair<Instruction, int>> pendingBranches = new List<KeyValuePair<Instruction, int>>();

    private readonly List<KeyValuePair<Instruction, int[]>> pendingSwitches = new List<KeyValuePair<Instruction, int[]>>();

    private readonly List<(ClauseKind Kind, int TryStart, int TryEnd, int HandlerStart, int HandlerEnd, Type? CatchType, int FilterStart)> clauses =
        new List<(ClauseKind, int, int, int, int, Type?, int)>();

    private readonly HashSet<(int Start, int End)> tryRanges = new HashSet<(int Start, int End)>();

    private bool sawCalli;

    private bool sharedTryRange;

    private bool disposed;

    internal MonoModScratchBody(DynamicMethodDefinition definition, MethodBase origin) {
        this.definition = definition;
        this.origin = origin;
        method = definition.Definition;
        module = method.Module;
        ExactScopes.Seed(module);
        body = method.Body;
        il = body.GetILProcessor();
    }

    public void InitLocals(bool value) {
        body.InitLocals = value;
    }

    public int DeclareLocal(Type type, bool pinned) {
        TypeReference imported = module.ImportReference(type);
        body.Variables.Add(new VariableDefinition(pinned ? new PinnedType(imported) : imported));
        return body.Variables.Count - 1;
    }

    public void MarkLabel(int label) {
        labelPositions[label] = body.Instructions.Count;
    }

    public void Emit(OpCode opcode) {
        Mono.Cecil.Cil.OpCode mapped = Map(opcode);
        if (mapped.OperandType != OperandType.InlineNone) {
            throw Refuse($"Opcode '{opcode.Name}' takes a {mapped.OperandType} operand, but it was emitted without one.");
        }

        il.Append(Instruction.Create(mapped));
    }

    public void Emit(OpCode opcode, int operand) {
        Mono.Cecil.Cil.OpCode mapped = Map(opcode);
        if (mapped.OperandType == OperandType.ShortInlineVar || mapped.OperandType == OperandType.InlineVar) {
            il.Append(Instruction.Create(mapped, Local(opcode, operand)));
            return;
        }

        if (mapped.OperandType == OperandType.ShortInlineArg || mapped.OperandType == OperandType.InlineArg) {
            il.Append(Instruction.Create(mapped, Parameter(opcode, operand)));
            return;
        }

        EmitInteger(opcode, mapped, operand);
    }

    public void Emit(OpCode opcode, long operand) {
        EmitInteger(opcode, Map(opcode), operand);
    }

    public void Emit(OpCode opcode, float operand) {
        EmitReal(opcode, Map(opcode), operand);
    }

    public void Emit(OpCode opcode, double operand) {
        EmitReal(opcode, Map(opcode), operand);
    }

    public void Emit(OpCode opcode, string operand) {
        il.Append(Instruction.Create(Map(opcode), operand));
    }

    public void Emit(OpCode opcode, Type operand) {
        il.Append(Instruction.Create(Map(opcode), module.ImportReference(operand)));
    }

    public void Emit(OpCode opcode, FieldInfo operand) {
        il.Append(Instruction.Create(Map(opcode), module.ImportReference(operand)));
    }

    public void Emit(OpCode opcode, MethodBase operand) {
        il.Append(Instruction.Create(Map(opcode), module.ImportReference(operand)));
    }

    public void EmitBranch(OpCode opcode, int label) {
        Instruction branch = Instruction.Create(Map(opcode), Instruction.Create(Mono.Cecil.Cil.OpCodes.Nop));
        pendingBranches.Add(new KeyValuePair<Instruction, int>(branch, label));
        il.Append(branch);
    }

    public void EmitSwitch(int[] labels) {
        Instruction jump = Instruction.Create(Mono.Cecil.Cil.OpCodes.Switch, Array.Empty<Instruction>());
        pendingSwitches.Add(new KeyValuePair<Instruction, int[]>(jump, labels));
        il.Append(jump);
    }

    public void EmitCalli(OpCode opcode, Type returnType, Type[] parameterTypes, int conventionCode, bool hasThis, bool explicitThis) {
        CallSite site = new CallSite(module.ImportReference(returnType)) {
            CallingConvention = (MethodCallingConvention)conventionCode,
            HasThis = hasThis,
            ExplicitThis = explicitThis,
        };

        foreach (Type parameter in parameterTypes) {
            site.Parameters.Add(new ParameterDefinition(module.ImportReference(parameter)));
        }

        sawCalli = true;
        il.Append(Instruction.Create(Map(opcode), site));
    }

    public void AddClause(ClauseKind kind, int tryStart, int tryEnd, int handlerStart, int handlerEnd, Type? catchType, int filterStart) {
        if (!tryRanges.Add((tryStart, tryEnd))) {
            sharedTryRange = true;
        }

        clauses.Add((kind, tryStart, tryEnd, handlerStart, handlerEnd, catchType, filterStart));
    }

    public MethodInfo Finish() {
        ResolveBranches();
        AttachClauses();
        ExactScopes.Bind(definition.Definition.Module);

        return sawCalli || sharedTryRange
            ? DMDGenerator<DMDCecilGenerator>.Generate(definition, null)
            : definition.Generate();
    }

    public void Dispose() {
        if (disposed) {
            return;
        }

        disposed = true;
        definition.Dispose();
    }

    private static Dictionary<string, Mono.Cecil.Cil.OpCode> BuildCecilOpCodeTable() {
        Dictionary<string, Mono.Cecil.Cil.OpCode> table = new Dictionary<string, Mono.Cecil.Cil.OpCode>(StringComparer.Ordinal);
        foreach (FieldInfo field in typeof(Mono.Cecil.Cil.OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)) {
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

    private static ExceptionHandlerType Kind(ClauseKind kind) {
        return kind switch {
            ClauseKind.Catch => ExceptionHandlerType.Catch,
            ClauseKind.Filter => ExceptionHandlerType.Filter,
            ClauseKind.Finally => ExceptionHandlerType.Finally,
            _ => ExceptionHandlerType.Fault,
        };
    }

    private ConcordEmitException Refuse(string because) {
        return new ConcordEmitException(
            RefusalCode,
            $"'{origin.DeclaringType?.FullName ?? "<no declaring type>"}.{origin.Name}' decoded to a body " +
            $"Cecil cannot carry: {because}");
    }

    private Mono.Cecil.Cil.OpCode Map(OpCode source) {
        if (source.Name is not null && CecilOpCodesByName.TryGetValue(source.Name, out Mono.Cecil.Cil.OpCode mapped)) {
            return mapped;
        }

        throw Refuse($"Unknown opcode '{source.Name}'. Use an OpCodes.* value from System.Reflection.Emit.");
    }

    private void EmitInteger(OpCode source, Mono.Cecil.Cil.OpCode mapped, long value) {
        switch (mapped.OperandType) {
            case OperandType.ShortInlineI:
                il.Append(mapped.Code == Code.Ldc_I4_S
                    ? Instruction.Create(mapped, checked((sbyte)value))
                    : Instruction.Create(mapped, checked((byte)value)));
                return;
            case OperandType.InlineI:
                il.Append(Instruction.Create(mapped, checked((int)value)));
                return;
            case OperandType.InlineI8:
                il.Append(Instruction.Create(mapped, value));
                return;
            case OperandType.ShortInlineR:
                il.Append(Instruction.Create(mapped, (float)value));
                return;
            case OperandType.InlineR:
                il.Append(Instruction.Create(mapped, (double)value));
                return;
            default:
                throw Refuse($"Opcode '{source.Name}' does not take an integer operand, but it was given '{value}'.");
        }
    }

    private void EmitReal(OpCode source, Mono.Cecil.Cil.OpCode mapped, double value) {
        switch (mapped.OperandType) {
            case OperandType.ShortInlineR:
                il.Append(Instruction.Create(mapped, (float)value));
                return;
            case OperandType.InlineR:
                il.Append(Instruction.Create(mapped, value));
                return;
            default:
                throw Refuse($"Opcode '{source.Name}' does not take a floating-point operand, but it was given '{value}'.");
        }
    }

    private VariableDefinition Local(OpCode source, int slot) {
        if (slot < 0 || slot >= body.Variables.Count) {
            throw Refuse($"Local slot {slot} referenced by opcode '{source.Name}' is out of range for a body with {body.Variables.Count} local(s).");
        }

        return body.Variables[slot];
    }

    private ParameterDefinition Parameter(OpCode source, int slot) {
        if (slot < 0 || slot >= method.Parameters.Count) {
            throw Refuse($"Argument slot {slot} referenced by opcode '{source.Name}' is out of range for a method with {method.Parameters.Count} parameter(s).");
        }

        return method.Parameters[slot];
    }

    private void ResolveBranches() {
        foreach (KeyValuePair<Instruction, int> branch in pendingBranches) {
            branch.Key.Operand = Target(branch.Value);
        }

        foreach (KeyValuePair<Instruction, int[]> jump in pendingSwitches) {
            Instruction[] targets = new Instruction[jump.Value.Length];
            for (int i = 0; i < targets.Length; i++) {
                targets[i] = Target(jump.Value[i]);
            }

            jump.Key.Operand = targets;
        }
    }

    private Instruction Target(int label) {
        if (!labelPositions.TryGetValue(label, out int position)) {
            throw Refuse($"Label L_{label:X4} is branched to but is not attached to any instruction.");
        }

        return At(position, "branch target");
    }

    private void AttachClauses() {
        foreach ((ClauseKind Kind, int TryStart, int TryEnd, int HandlerStart, int HandlerEnd, Type? CatchType, int FilterStart) clause in clauses) {
            ExceptionHandler handler = new ExceptionHandler(Kind(clause.Kind)) {
                TryStart = At(clause.TryStart, "try start"),
                TryEnd = At(clause.TryEnd, "try end"),
                HandlerStart = At(clause.HandlerStart, "handler start"),
                HandlerEnd = clause.HandlerEnd >= body.Instructions.Count ? null : At(clause.HandlerEnd, "handler end"),
            };

            if (clause.Kind == ClauseKind.Catch) {
                handler.CatchType = module.ImportReference(
                    clause.CatchType ?? throw Refuse("a catch clause carries no caught type."));
            }

            if (clause.Kind == ClauseKind.Filter) {
                handler.FilterStart = At(clause.FilterStart, "filter start");
            }

            body.ExceptionHandlers.Add(handler);
        }
    }

    private Instruction At(int position, string role) {
        if (position < 0 || position >= body.Instructions.Count) {
            throw Refuse($"a {role} is instruction {position} of {body.Instructions.Count}.");
        }

        return body.Instructions[position];
    }
}
