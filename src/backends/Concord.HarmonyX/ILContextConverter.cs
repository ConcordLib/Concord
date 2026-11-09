using System.Globalization;
using System.Reflection;
using Concord.Emit;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;
using CecilInstruction = Mono.Cecil.Cil.Instruction;
using CecilOpCode = Mono.Cecil.Cil.OpCode;
using CecilOpCodes = Mono.Cecil.Cil.OpCodes;
using CecilOperandType = Mono.Cecil.Cil.OperandType;
using SreOpCode = System.Reflection.Emit.OpCode;
using SreOpCodes = System.Reflection.Emit.OpCodes;

namespace Concord.HarmonyX;

internal static class ILContextConverter {
    internal const string CalliCode = "CONC124";

    internal const string BlockCode = "CONC125";

    internal const string ShapeCode = "CONC185";

    private const string EvictedMarker = "[Concord.HarmonyX] injection-evicted";

    private static readonly Dictionary<string, SreOpCode> SreByName = BuildSreTable();

    private static readonly Dictionary<string, CecilOpCode> CecilByName = BuildCecilTable();

    private static readonly Dictionary<string, string> LongFormByShortName = BuildLongFormTable();

    internal static void Compose(
        MethodBase target,
        IReadOnlyList<Injection> ordered,
        ILContext il,
        Action<string> report) {
        ITranspilerContext context = WrapperComposer.CreateStreamContext(target);
        StreamState state = Read(il, context);

        List<CodeInstruction> composed = WrapperComposer.TransformStream(
            target,
            state.Stream,
            ordered,
            context,
            out IReadOnlyList<RejectedInjection> rejected);

        foreach (RejectedInjection rejection in rejected) {
            if (EvictionReport.Compose(EvictedMarker, target, rejection) is string line) {
                report(line);
            }
        }

        Write(il, composed, state);
    }

    private static StreamState Read(ILContext il, ITranspilerContext context) {
        StreamState state = new StreamState();
        foreach (VariableDefinition variable in il.Body.Variables) {
            state.LocalBySlot.Add(context.DeclareLocal(SlotType(variable)));
        }

        state.OriginalLocalCount = state.LocalBySlot.Count;

        List<CecilInstruction> source = new List<CecilInstruction>(il.Instrs);
        Dictionary<CecilInstruction, int> indexOf = new Dictionary<CecilInstruction, int>(source.Count);
        for (int i = 0; i < source.Count; i++) {
            indexOf[source[i]] = i;
        }

        Dictionary<int, Concord.Label> labelByIndex = new Dictionary<int, Concord.Label>();

        Concord.Label LabelAt(CecilInstruction? instruction) {
            int index = Index(indexOf, instruction, "branch target");
            if (labelByIndex.TryGetValue(index, out Concord.Label existing)) {
                return existing;
            }

            Concord.Label minted = context.DefineLabel();
            labelByIndex[index] = minted;
            return minted;
        }

        List<CodeInstruction> stream = new List<CodeInstruction>(source.Count);
        foreach (CecilInstruction instruction in source) {
            stream.Add(new CodeInstruction(MapToSre(instruction.OpCode), ReadOperand(il, instruction, LabelAt, state)));
        }

        foreach (KeyValuePair<int, Concord.Label> entry in labelByIndex) {
            stream[entry.Key].labels.Add(entry.Value);
        }

        state.Stream = stream;
        ReadBlocks(il, state, indexOf, context);
        return state;
    }

    private static Type SlotType(VariableDefinition variable) {
        TypeReference declared = variable.VariableType;
        return (declared is PinnedType pinned ? pinned.ElementType : declared).ResolveReflection();
    }

    private static object? ReadOperand(
        ILContext il,
        CecilInstruction instruction,
        Func<CecilInstruction?, Concord.Label> labelAt,
        StreamState state) {
        switch (instruction.Operand) {
            case null:
                return null;
            case ILLabel label:
                return labelAt(label.Target);
            case ILLabel[] table:
                return Labels(table, labelAt);
            case CecilInstruction branch:
                return labelAt(branch);
            case CecilInstruction[] rawTable:
                return Labels(rawTable, labelAt);
            case VariableDefinition variable:
                return state.LocalBySlot[variable.Index];
            case ParameterDefinition parameter:
                return parameter.Index + (il.Method.HasThis ? 1 : 0);
            case Mono.Cecil.CallSite:
                throw new ConcordEmitException(
                    CalliCode,
                    "The HarmonyX backend cannot compose onto a body that contains a calli instruction. Concord carries a " +
                    "call-site signature in a type whose constructors it does not expose, so the backend has no way to build " +
                    "one from the host's Cecil call site.");
            case MethodReference method:
                return method.ResolveReflection();
            case FieldReference field:
                return field.ResolveReflection();
            case TypeReference type:
                return type.ResolveReflection();
            default:
                return instruction.Operand;
        }
    }

    private static Concord.Label[] Labels(ILLabel[] table, Func<CecilInstruction?, Concord.Label> labelAt) {
        Concord.Label[] labels = new Concord.Label[table.Length];
        for (int i = 0; i < labels.Length; i++) {
            labels[i] = labelAt(table[i].Target);
        }

        return labels;
    }

    private static Concord.Label[] Labels(CecilInstruction[] table, Func<CecilInstruction?, Concord.Label> labelAt) {
        Concord.Label[] labels = new Concord.Label[table.Length];
        for (int i = 0; i < labels.Length; i++) {
            labels[i] = labelAt(table[i]);
        }

        return labels;
    }

    private static void ReadBlocks(
        ILContext il,
        StreamState state,
        Dictionary<CecilInstruction, int> indexOf,
        ITranspilerContext context) {
        if (il.Body.ExceptionHandlers.Count == 0) {
            return;
        }

        List<CodeInstruction> stream = state.Stream;
        List<Region> regions = GroupRegions(il, indexOf, state, context);

        Dictionary<int, List<ExceptionBlock>> ends = new Dictionary<int, List<ExceptionBlock>>();
        Dictionary<int, List<ExceptionBlock>> handlerBegins = new Dictionary<int, List<ExceptionBlock>>();
        Dictionary<int, List<ExceptionBlock>> tryBegins = new Dictionary<int, List<ExceptionBlock>>();

        regions.Sort((left, right) => (right.End - right.TryStart).CompareTo(left.End - left.TryStart));

        foreach (Region region in regions) {
            Add(tryBegins, region.TryStart, new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
            foreach (ExceptionHandler handler in region.Handlers) {
                if (handler.HandlerType == ExceptionHandlerType.Filter) {
                    Add(
                        handlerBegins,
                        Index(indexOf, handler.FilterStart, "filter start"),
                        new ExceptionBlock(ExceptionBlockType.BeginExceptFilterBlock));
                }

                Add(
                    handlerBegins,
                    Index(indexOf, handler.HandlerStart, "handler start"),
                    new ExceptionBlock(BeginKind(handler), handler.CatchType?.ResolveReflection()));
            }

            Add(ends, region.End, new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));
        }

        for (int i = 0; i < stream.Count; i++) {
            Append(stream[i], ends, i);
            Append(stream[i], handlerBegins, i);
            Append(stream[i], tryBegins, i);
        }
    }

    private static List<Region> GroupRegions(
        ILContext il,
        Dictionary<CecilInstruction, int> indexOf,
        StreamState state,
        ITranspilerContext context) {
        Dictionary<(int Start, int End), Region> byExtent = new Dictionary<(int Start, int End), Region>();
        List<Region> regions = new List<Region>();

        foreach (ExceptionHandler handler in il.Body.ExceptionHandlers) {
            int tryStart = Index(indexOf, handler.TryStart, "try start");
            int tryEnd = Index(indexOf, handler.TryEnd, "try end");
            int end = handler.HandlerEnd is null
                ? OpenEnd(state, context)
                : Index(indexOf, handler.HandlerEnd, "handler end");

            if (byExtent.TryGetValue((tryStart, tryEnd), out Region? shared)) {
                shared.Handlers.Add(handler);
                shared.End = Math.Max(shared.End, end);
                continue;
            }

            Region region = new Region(tryStart, end);
            region.Handlers.Add(handler);
            byExtent[(tryStart, tryEnd)] = region;
            regions.Add(region);
        }

        return regions;
    }

    private static int OpenEnd(StreamState state, ITranspilerContext context) {
        if (state.OpenEndIndex is int already) {
            return already;
        }

        CodeInstruction anchor = new CodeInstruction(SreOpCodes.Nop);
        anchor.labels.Add(context.DefineLabel());
        state.Stream.Add(anchor);
        state.OpenEndIndex = state.Stream.Count - 1;
        return state.OpenEndIndex.Value;
    }

    private static ExceptionBlockType BeginKind(ExceptionHandler handler) {
        return handler.HandlerType switch {
            ExceptionHandlerType.Catch => ExceptionBlockType.BeginCatchBlock,
            ExceptionHandlerType.Filter => ExceptionBlockType.BeginCatchBlock,
            ExceptionHandlerType.Finally => ExceptionBlockType.BeginFinallyBlock,
            ExceptionHandlerType.Fault => ExceptionBlockType.BeginFaultBlock,
            _ => throw new ConcordEmitException(BlockCode, $"Unrecognized Cecil exception handler kind '{handler.HandlerType}'."),
        };
    }

    private static void Write(ILContext il, List<CodeInstruction> composed, StreamState state) {
        foreach (Type? type in NewLocalTypes(composed, state.OriginalLocalCount)) {
            il.Body.Variables.Add(new VariableDefinition(il.Import(type ?? typeof(object))));
            il.Body.InitLocals = true;
        }

        List<CecilInstruction> emitted = new List<CecilInstruction>(composed.Count);
        List<KeyValuePair<CecilInstruction, Concord.Label>> branches = new List<KeyValuePair<CecilInstruction, Concord.Label>>();
        List<KeyValuePair<CecilInstruction, Concord.Label[]>> tables = new List<KeyValuePair<CecilInstruction, Concord.Label[]>>();
        Dictionary<Concord.Label, int> indexByLabel = new Dictionary<Concord.Label, int>();

        for (int i = 0; i < composed.Count; i++) {
            foreach (Concord.Label label in composed[i].labels) {
                indexByLabel[label] = i;
            }
        }

        foreach (CodeInstruction instruction in composed) {
            emitted.Add(Build(il, instruction, branches, tables));
        }

        foreach (KeyValuePair<CecilInstruction, Concord.Label> branch in branches) {
            branch.Key.Operand = emitted[Target(indexByLabel, branch.Value, emitted.Count)];
        }

        foreach (KeyValuePair<CecilInstruction, Concord.Label[]> table in tables) {
            CecilInstruction[] targets = new CecilInstruction[table.Value.Length];
            for (int i = 0; i < targets.Length; i++) {
                targets[i] = emitted[Target(indexByLabel, table.Value[i], emitted.Count)];
            }

            table.Key.Operand = targets;
        }

        List<ExceptionHandler> handlers = BuildHandlers(il, composed, emitted);

        il.Instrs.Clear();
        foreach (CecilInstruction instruction in emitted) {
            il.Instrs.Add(instruction);
        }

        il.Body.ExceptionHandlers.Clear();
        foreach (ExceptionHandler handler in handlers) {
            il.Body.ExceptionHandlers.Add(handler);
        }
    }

    private static List<Type?> NewLocalTypes(List<CodeInstruction> composed, int originalCount) {
        Dictionary<int, Type> byIndex = new Dictionary<int, Type>();
        int highest = originalCount - 1;
        foreach (CodeInstruction instruction in composed) {
            if (instruction.operand is not LocalRef local || local.Index < originalCount) {
                continue;
            }

            byIndex[local.Index] = local.Type;
            highest = Math.Max(highest, local.Index);
        }

        List<Type?> added = new List<Type?>();
        for (int slot = originalCount; slot <= highest; slot++) {
            added.Add(byIndex.TryGetValue(slot, out Type? type) ? type : null);
        }

        return added;
    }

    private static CecilInstruction Build(
        ILContext il,
        CodeInstruction instruction,
        List<KeyValuePair<CecilInstruction, Concord.Label>> branches,
        List<KeyValuePair<CecilInstruction, Concord.Label[]>> tables) {
        CecilOpCode opcode = MapToCecil(Widen(instruction.opcode));

        switch (instruction.operand) {
            case Concord.Label label: {
                CecilInstruction branch = CecilInstruction.Create(opcode, CecilInstruction.Create(CecilOpCodes.Nop));
                branches.Add(new KeyValuePair<CecilInstruction, Concord.Label>(branch, label));
                return branch;
            }

            case Concord.Label[] labels: {
                CecilInstruction table = CecilInstruction.Create(CecilOpCodes.Switch, Array.Empty<CecilInstruction>());
                tables.Add(new KeyValuePair<CecilInstruction, Concord.Label[]>(table, labels));
                return table;
            }

            case LocalRef local:
                return CecilInstruction.Create(opcode, Variable(il, local.Index));
            case Type type:
                return CecilInstruction.Create(opcode, il.Import(type));
            case FieldInfo field:
                return CecilInstruction.Create(opcode, il.Import(field));
            case MethodBase method:
                return CecilInstruction.Create(opcode, il.Import(method));
            case string text:
                return CecilInstruction.Create(opcode, text);
            default:
                return BuildLiteral(il, opcode, instruction);
        }
    }

    private static CecilInstruction BuildLiteral(ILContext il, CecilOpCode opcode, CodeInstruction instruction) {
        if (instruction.operand is null || opcode.OperandType == CecilOperandType.InlineNone) {
            return CecilInstruction.Create(opcode);
        }

        switch (opcode.OperandType) {
            case CecilOperandType.InlineVar:
            case CecilOperandType.ShortInlineVar:
                return CecilInstruction.Create(opcode, Variable(il, Slot(instruction)));
            case CecilOperandType.InlineArg:
            case CecilOperandType.ShortInlineArg:
                return CecilInstruction.Create(opcode, Parameter(il, Slot(instruction)));
            case CecilOperandType.ShortInlineI:
                return opcode.Code == Code.Ldc_I4_S
                    ? CecilInstruction.Create(opcode, checked((sbyte)Slot(instruction)))
                    : CecilInstruction.Create(opcode, checked((byte)Slot(instruction)));
            case CecilOperandType.InlineI:
                return CecilInstruction.Create(opcode, Slot(instruction));
            case CecilOperandType.InlineI8:
                return CecilInstruction.Create(opcode, Convert.ToInt64(instruction.operand, CultureInfo.InvariantCulture));
            case CecilOperandType.ShortInlineR:
                return CecilInstruction.Create(opcode, Convert.ToSingle(instruction.operand, CultureInfo.InvariantCulture));
            case CecilOperandType.InlineR:
                return CecilInstruction.Create(opcode, Convert.ToDouble(instruction.operand, CultureInfo.InvariantCulture));
            default:
                throw new ConcordEmitException(
                    ShapeCode,
                    $"Opcode '{instruction.opcode.Name}' carries operand '{instruction.operand}' of type " +
                    $"'{instruction.operand.GetType()}', which the HarmonyX backend cannot write as Cecil IL.");
        }
    }

    private static int Slot(CodeInstruction instruction) {
        return Convert.ToInt32(instruction.operand, CultureInfo.InvariantCulture);
    }

    private static VariableDefinition Variable(ILContext il, int slot) {
        if (slot < 0 || slot >= il.Body.Variables.Count) {
            throw new ConcordEmitException(
                ShapeCode,
                $"Local slot {slot} is referenced by the composed body, which declares {il.Body.Variables.Count} local(s).");
        }

        return il.Body.Variables[slot];
    }

    private static ParameterDefinition Parameter(ILContext il, int slot) {
        int index = slot - (il.Method.HasThis ? 1 : 0);
        if (index < 0 || index >= il.Method.Parameters.Count) {
            throw new ConcordEmitException(
                ShapeCode,
                $"Argument slot {slot} is referenced by the composed body, which takes {il.Method.Parameters.Count} argument(s).");
        }

        return il.Method.Parameters[index];
    }

    private static List<ExceptionHandler> BuildHandlers(ILContext il, List<CodeInstruction> composed, List<CecilInstruction> emitted) {
        List<ExceptionHandler> handlers = new List<ExceptionHandler>();
        Stack<OpenRegion> open = new Stack<OpenRegion>();

        for (int i = 0; i < composed.Count; i++) {
            foreach (ExceptionBlock block in composed[i].blocks) {
                switch (block.blockType) {
                    case ExceptionBlockType.BeginExceptionBlock:
                        open.Push(new OpenRegion(i));
                        break;
                    case ExceptionBlockType.BeginExceptFilterBlock:
                        Current(open).OpenFilter(i);
                        break;
                    case ExceptionBlockType.BeginCatchBlock:
                        Current(open).OpenHandler(ExceptionHandlerType.Catch, i, block.catchType);
                        break;
                    case ExceptionBlockType.BeginFinallyBlock:
                        Current(open).OpenHandler(ExceptionHandlerType.Finally, i, null);
                        break;
                    case ExceptionBlockType.BeginFaultBlock:
                        Current(open).OpenHandler(ExceptionHandlerType.Fault, i, null);
                        break;
                    case ExceptionBlockType.EndExceptionBlock:
                        handlers.AddRange(Current(open).Close(il, i, emitted));
                        open.Pop();
                        break;
                    default:
                        throw new ConcordEmitException(BlockCode, $"Unrecognized Concord exception block kind '{block.blockType}'.");
                }
            }
        }

        while (open.Count > 0) {
            handlers.AddRange(open.Peek().Close(il, composed.Count, emitted));
            open.Pop();
        }

        return handlers;
    }

    private static OpenRegion Current(Stack<OpenRegion> open) {
        return open.Count > 0
            ? open.Peek()
            : throw new ConcordEmitException(
                BlockCode,
                "The composed body closes or extends an exception region that was never opened.");
    }

    private static int Target(Dictionary<Concord.Label, int> indexByLabel, Concord.Label label, int count) {
        if (!indexByLabel.TryGetValue(label, out int index)) {
            throw new ConcordEmitException(ShapeCode, $"Label {label} is branched to but is not attached to any instruction.");
        }

        return index < count
            ? index
            : throw new ConcordEmitException(ShapeCode, $"Label {label} lands past the end of the composed body.");
    }

    private static int Index(Dictionary<CecilInstruction, int> indexOf, CecilInstruction? instruction, string role) {
        if (instruction is not null && indexOf.TryGetValue(instruction, out int index)) {
            return index;
        }

        throw new ConcordEmitException(
            ShapeCode,
            $"The host handed Concord a body whose {role} is not one of the body's own instructions.");
    }

    private static void Add(Dictionary<int, List<ExceptionBlock>> markers, int index, ExceptionBlock block) {
        if (!markers.TryGetValue(index, out List<ExceptionBlock>? at)) {
            at = new List<ExceptionBlock>();
            markers[index] = at;
        }

        at.Add(block);
    }

    private static void Append(CodeInstruction instruction, Dictionary<int, List<ExceptionBlock>> markers, int index) {
        if (markers.TryGetValue(index, out List<ExceptionBlock>? at)) {
            instruction.blocks.AddRange(at);
        }
    }

    private static SreOpCode Widen(SreOpCode opcode) {
        return opcode.Name is string name && LongFormByShortName.TryGetValue(name, out string? longForm)
            ? SreByName[longForm]
            : opcode;
    }

    private static SreOpCode MapToSre(CecilOpCode opcode) {
        string name = opcode.Name switch {
            "ldelem.any" => "ldelem",
            "stelem.any" => "stelem",
            _ => opcode.Name,
        };

        return SreByName.TryGetValue(name, out SreOpCode mapped)
            ? mapped
            : throw new ConcordEmitException(ShapeCode, $"Cecil opcode '{opcode.Name}' has no System.Reflection.Emit equivalent.");
    }

    private static CecilOpCode MapToCecil(SreOpCode opcode) {
        return opcode.Name is string name && CecilByName.TryGetValue(name, out CecilOpCode mapped)
            ? mapped
            : throw new ConcordEmitException(ShapeCode, $"Opcode '{opcode.Name}' has no Cecil equivalent.");
    }

    private static Dictionary<string, SreOpCode> BuildSreTable() {
        Dictionary<string, SreOpCode> table = new Dictionary<string, SreOpCode>(StringComparer.Ordinal);
        foreach (FieldInfo field in typeof(SreOpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)) {
            if (field.FieldType != typeof(SreOpCode)) {
                continue;
            }

            SreOpCode opcode = (SreOpCode)field.GetValue(null)!;
            if (opcode.Name is string name) {
                table[name] = opcode;
            }
        }

        return table;
    }

    private static Dictionary<string, CecilOpCode> BuildCecilTable() {
        Dictionary<string, CecilOpCode> table = new Dictionary<string, CecilOpCode>(StringComparer.Ordinal);
        foreach (FieldInfo field in typeof(CecilOpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)) {
            if (field.FieldType != typeof(CecilOpCode)) {
                continue;
            }

            CecilOpCode opcode = (CecilOpCode)field.GetValue(null)!;
            if (opcode.Name is string name) {
                table[name] = opcode;
            }
        }

        table["ldelem"] = CecilOpCodes.Ldelem_Any;
        table["stelem"] = CecilOpCodes.Stelem_Any;
        return table;
    }

    private static Dictionary<string, string> BuildLongFormTable() {
        return new Dictionary<string, string>(StringComparer.Ordinal) {
            ["br.s"] = "br",
            ["brfalse.s"] = "brfalse",
            ["brtrue.s"] = "brtrue",
            ["beq.s"] = "beq",
            ["bge.s"] = "bge",
            ["bgt.s"] = "bgt",
            ["ble.s"] = "ble",
            ["blt.s"] = "blt",
            ["bne.un.s"] = "bne.un",
            ["bge.un.s"] = "bge.un",
            ["bgt.un.s"] = "bgt.un",
            ["ble.un.s"] = "ble.un",
            ["blt.un.s"] = "blt.un",
            ["leave.s"] = "leave",
            ["ldloc.s"] = "ldloc",
            ["ldloca.s"] = "ldloca",
            ["stloc.s"] = "stloc",
            ["ldarg.s"] = "ldarg",
            ["ldarga.s"] = "ldarga",
            ["starg.s"] = "starg",
        };
    }

    private sealed class StreamState {
        internal List<LocalRef> LocalBySlot { get; } = new List<LocalRef>();

        internal List<CodeInstruction> Stream { get; set; } = new List<CodeInstruction>();

        internal int OriginalLocalCount { get; set; }

        internal int? OpenEndIndex { get; set; }
    }

    private sealed class Region {
        internal Region(int tryStart, int end) {
            TryStart = tryStart;
            End = end;
        }

        internal int TryStart { get; }

        internal int End { get; set; }

        internal List<ExceptionHandler> Handlers { get; } = new List<ExceptionHandler>();
    }

    private sealed class OpenRegion {
        private readonly int tryStart;

        private readonly List<Entry> entries = new List<Entry>();

        private int tryEnd = -1;

        private int pendingFilter = -1;

        internal OpenRegion(int tryStart) {
            this.tryStart = tryStart;
        }

        internal void OpenFilter(int index) {
            CloseLast(index);
            pendingFilter = index;
        }

        internal void OpenHandler(ExceptionHandlerType kind, int index, Type? caught) {
            CloseLast(index);
            entries.Add(new Entry(
                pendingFilter >= 0 ? ExceptionHandlerType.Filter : kind,
                pendingFilter,
                index,
                caught));
            pendingFilter = -1;
        }

        internal List<ExceptionHandler> Close(ILContext il, int index, List<CecilInstruction> emitted) {
            CloseLast(index);
            if (tryEnd < 0 || tryEnd >= emitted.Count) {
                throw new ConcordEmitException(BlockCode, "The composed body opens an exception region that never reaches a handler.");
            }

            List<ExceptionHandler> built = new List<ExceptionHandler>(entries.Count);
            foreach (Entry entry in entries) {
                ExceptionHandler handler = new ExceptionHandler(entry.Kind) {
                    TryStart = emitted[tryStart],
                    TryEnd = emitted[tryEnd],
                    HandlerStart = emitted[entry.HandlerStart],
                    HandlerEnd = entry.HandlerEnd >= 0 && entry.HandlerEnd < emitted.Count ? emitted[entry.HandlerEnd] : null,
                };

                if (entry.Kind == ExceptionHandlerType.Filter) {
                    handler.FilterStart = emitted[entry.FilterStart];
                }

                if (entry.Caught is Type caught) {
                    handler.CatchType = il.Import(caught);
                }

                built.Add(handler);
            }

            return built;
        }

        private void CloseLast(int index) {
            if (tryEnd < 0) {
                tryEnd = index;
            }

            if (entries.Count > 0 && entries[entries.Count - 1].HandlerEnd < 0) {
                entries[entries.Count - 1].HandlerEnd = index;
            }
        }

        private sealed class Entry {
            internal Entry(ExceptionHandlerType kind, int filterStart, int handlerStart, Type? caught) {
                Kind = kind;
                FilterStart = filterStart;
                HandlerStart = handlerStart;
                Caught = caught;
                HandlerEnd = -1;
            }

            internal ExceptionHandlerType Kind { get; }

            internal int FilterStart { get; }

            internal int HandlerStart { get; }

            internal Type? Caught { get; }

            internal int HandlerEnd { get; set; }
        }
    }
}
