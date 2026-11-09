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
    private static readonly Dictionary<string, OpCode> OpCodesByName = BuildOpCodeTable();

    public static List<CodeInstruction> ToInstructions(MethodDefinition definition, TranspilerContext context) {
        return ToInstructions(definition, context, null, 0);
    }

    // preservedLabels/firstFreshLabelId let a caller correlate labels it already knows about (e.g.
    // ids it minted on the same context before composing) with whatever instruction they land on
    // after composition, instead of every branch target getting a brand-new context-local id. See
    // WrapperComposer.TransformStream, the sole caller that passes non-default values here.
    public static List<CodeInstruction> ToInstructions(MethodDefinition definition, TranspilerContext context, Dictionary<Instruction, List<int>>? preservedLabels, int firstFreshLabelId) {
        MethodBody body = definition.Body;
        context.ExistingLocalCount = body.Variables.Count;
        context.NextLabelId = Math.Max(context.NextLabelId, firstFreshLabelId);

        context.ExistingLocalTypes.Clear();
        foreach (VariableDefinition existing in body.Variables) {
            context.ExistingLocalTypes.Add(ResolveType(existing.VariableType));
        }

        AssignLabels(body, context, preservedLabels);

        List<CodeInstruction> instructions = new List<CodeInstruction>(body.Instructions.Count);
        Dictionary<Instruction, CodeInstruction> byInstruction = new Dictionary<Instruction, CodeInstruction>(body.Instructions.Count);

        foreach (Instruction instruction in body.Instructions) {
            CodeInstruction converted = new CodeInstruction(MapOpCode(instruction.OpCode), MapOperandToClr(instruction, context));
            InstructionOrigins.CarryOver(instruction, converted);
            if (context.LabelsByInstruction.TryGetValue(instruction, out Label label)) {
                converted.labels.Add(label);
            }

            instructions.Add(converted);
            byInstruction[instruction] = converted;
        }

        AttachExceptionBlocks(body, byInstruction, context);

        return instructions;
    }

    private static Dictionary<string, OpCode> BuildOpCodeTable() {
        Dictionary<string, OpCode> table = new Dictionary<string, OpCode>(StringComparer.Ordinal);
        FieldInfo[] fields = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static);
        foreach (FieldInfo field in fields) {
            if (field.FieldType != typeof(OpCode)) {
                continue;
            }

            OpCode opcode = (OpCode)field.GetValue(null)!;
            if (opcode.Name is not null) {
                table[opcode.Name] = opcode;
            }
        }

        table["ldelem.any"] = OpCodes.Ldelem;
        table["stelem.any"] = OpCodes.Stelem;

        return table;
    }

    private static OpCode MapOpCode(Mono.Cecil.Cil.OpCode source) {
        if (OpCodesByName.TryGetValue(source.Name, out OpCode mapped)) {
            return mapped;
        }

        throw new ConcordEmitException(CodeConversionFailed, $"Unknown opcode '{source.Name}'. Use an OpCodes.* value from System.Reflection.Emit.");
    }

    private static void AssignLabels(MethodBody body, TranspilerContext context, Dictionary<Instruction, List<int>>? preservedLabels) {
        foreach (Instruction instruction in body.Instructions) {
            if (instruction.Operand is Instruction branchTarget) {
                EnsureLabel(branchTarget, context, preservedLabels);
            } else if (instruction.Operand is Instruction[] switchTargets) {
                foreach (Instruction switchTarget in switchTargets) {
                    EnsureLabel(switchTarget, context, preservedLabels);
                }
            }
        }

        foreach (ExceptionHandler handler in body.ExceptionHandlers) {
            EnsureLabelIfPresent(handler.TryStart, context, preservedLabels);
            EnsureLabelIfPresent(handler.TryEnd, context, preservedLabels);
            EnsureLabelIfPresent(handler.HandlerStart, context, preservedLabels);
            EnsureLabelIfPresent(handler.HandlerEnd, context, preservedLabels);
            EnsureLabelIfPresent(handler.FilterStart, context, preservedLabels);
        }
    }

    // Which boundaries an ExceptionHandler actually names depends on its kind: only a filter has
    // FilterStart, only a guarded region has TryEnd, and so on. The unset ones are simply skipped.
    private static void EnsureLabelIfPresent(Instruction? target, TranspilerContext context, Dictionary<Instruction, List<int>>? preservedLabels) {
        if (target is not null) {
            EnsureLabel(target, context, preservedLabels);
        }
    }

    // Reuses the caller's own id for a target this context already knows about (from preservedLabels,
    // built by WriteBack from the labels the caller attached before composing) instead of always
    // minting a fresh context-local one. Falls through to a fresh id, seeded past firstFreshLabelId by
    // the caller in ToInstructions, for any target with no such provenance.
    private static void EnsureLabel(Instruction target, TranspilerContext context, Dictionary<Instruction, List<int>>? preservedLabels) {
        if (context.LabelsByInstruction.ContainsKey(target)) {
            return;
        }

        if (preservedLabels is not null && preservedLabels.TryGetValue(target, out List<int>? ids) && ids.Count > 0) {
            context.LabelsByInstruction[target] = LabelFactory.Create(ids[0]);
            return;
        }

        context.LabelsByInstruction[target] = context.DefineLabel();
    }

    private static object? MapOperandToClr(Instruction instruction, TranspilerContext context) {
        return instruction.Operand switch {
            null => null,
            Instruction target => LabelOf(target, context, "a branch target"),
            Instruction[] targets => MapSwitchTargets(targets, context),
            VariableDefinition variable => LocalRefFactory.Create(variable.Index, ResolveType(variable.VariableType)),
            ParameterDefinition parameter => MapArgumentOperand(instruction.OpCode.OperandType, parameter),
            CallSite callSite => new CallSiteSignature(callSite),
            TypeReference type => ResolveType(type),
            FieldReference field => ResolveField(field),
            MethodReference method => ResolveMethod(method),
            _ => instruction.Operand,
        };
    }

    private static Label[] MapSwitchTargets(Instruction[] targets, TranspilerContext context) {
        Label[] labels = new Label[targets.Length];
        for (int i = 0; i < targets.Length; i++) {
            labels[i] = LabelOf(targets[i], context, "a switch target");
        }

        return labels;
    }

    private static object MapArgumentOperand(OperandType operandType, ParameterDefinition parameter) {
        return operandType == OperandType.ShortInlineArg ? (byte)parameter.Index : parameter.Index;
    }

    private static Type ResolveType(TypeReference typeReference) {
        return typeReference.ResolveReflection() ?? throw new ConcordEmitException(CodeConversionFailed, $"Could not resolve CLR type for '{typeReference.FullName}'. Pass a typeof(...) the mod can reference, and make sure its assembly is loaded before the patch is applied.");
    }

    private static FieldInfo ResolveField(FieldReference fieldReference) {
        return fieldReference.ResolveReflection() ?? throw new ConcordEmitException(CodeConversionFailed, $"Could not resolve CLR field for '{fieldReference.FullName}'. Pass a FieldInfo from a loaded type. A name-only reference is not enough.");
    }

    private static MethodBase ResolveMethod(MethodReference methodReference) {
        return methodReference.ResolveReflection() ?? throw new ConcordEmitException(CodeConversionFailed, $"Could not resolve CLR method for '{methodReference.FullName}'. Pass a MethodInfo or ConstructorInfo from a loaded type. A name-only reference is not enough.");
    }

    private static void AttachExceptionBlocks(MethodBody body, Dictionary<Instruction, CodeInstruction> byInstruction, TranspilerContext context) {
        if (body.ExceptionHandlers.Count == 0) {
            return;
        }

        Instruction lastInstruction = body.Instructions[body.Instructions.Count - 1];
        Dictionary<Instruction, int> indexOf = new Dictionary<Instruction, int>(body.Instructions.Count);
        int index = 0;
        foreach (Instruction instruction in body.Instructions) {
            indexOf[instruction] = index++;
        }

        List<ExceptionEnvelope> envelopes = BuildEnvelopes(body.ExceptionHandlers, indexOf, lastInstruction, body.Instructions.Count);
        List<ExceptionEnvelope> roots = LinkEnvelopes(envelopes);
        roots.Sort((a, b) => a.TryStartIndex.CompareTo(b.TryStartIndex));

        foreach (ExceptionEnvelope root in roots) {
            EmitEnvelope(root, byInstruction, indexOf, context);
        }
    }

    // Cecil lists exception handlers innermost-first, but two handlers can legitimately share the
    // same TryStart while protecting different-sized regions - a try/catch/finally is TWO nested
    // envelopes (the finally's protected range encloses the try body AND the catch handler), not one
    // flat group. Grouping must key on the exact (TryStart, TryEnd) pair - true multi-catch siblings
    // share both; a catch nested inside an enclosing finally does not.
    //
    // HandlerEnd is legally null when a handler runs to the literal end of the method body (no
    // trailing instruction exists to mark where it stops). That envelope's real extent is "one past
    // the last instruction" for containment purposes - using the last real instruction's index here
    // would make it indistinguishable from (and possibly subordinate to) a sibling envelope that
    // genuinely ends at that same last instruction, so the synthetic index must be strictly greater
    // than every real index (instructionCount, never a valid index into body.Instructions).
    private static List<ExceptionEnvelope> BuildEnvelopes(IEnumerable<ExceptionHandler> handlers, Dictionary<Instruction, int> indexOf, Instruction lastInstruction, int instructionCount) {
        Dictionary<(Instruction TryStart, Instruction TryEnd), List<ExceptionHandler>> byRange = new Dictionary<(Instruction, Instruction), List<ExceptionHandler>>();
        List<(Instruction TryStart, Instruction TryEnd)> order = new List<(Instruction, Instruction)>();

        foreach (ExceptionHandler handler in handlers) {
            (Instruction TryStart, Instruction TryEnd) key = (handler.TryStart, handler.TryEnd);
            if (!byRange.TryGetValue(key, out List<ExceptionHandler>? group)) {
                group = new List<ExceptionHandler>();
                byRange[key] = group;
                order.Add(key);
            }

            group.Add(handler);
        }

        List<ExceptionEnvelope> envelopes = new List<ExceptionEnvelope>();
        foreach ((Instruction TryStart, Instruction TryEnd) key in order) {
            List<ExceptionHandler> group = byRange[key];
            group.Sort((a, b) => HandlerOpenIndex(a, indexOf).CompareTo(HandlerOpenIndex(b, indexOf)));

            Instruction endInstruction = key.TryStart;
            int endIndex = -1;
            bool endsAtBodyEnd = false;
            foreach (ExceptionHandler handler in group) {
                bool handlerEndsAtBodyEnd = handler.HandlerEnd is null;
                Instruction handlerEndInstruction = handlerEndsAtBodyEnd ? lastInstruction : handler.HandlerEnd!;
                int handlerEndIndex = handlerEndsAtBodyEnd ? instructionCount : IndexOf(handlerEndInstruction, indexOf, handler, "HandlerEnd");
                if (handlerEndIndex > endIndex) {
                    endIndex = handlerEndIndex;
                    endInstruction = handlerEndInstruction;
                    endsAtBodyEnd = handlerEndsAtBodyEnd;
                }
            }

            envelopes.Add(new ExceptionEnvelope(group, key.TryStart, IndexOf(key.TryStart, indexOf, group[0], "TryStart"), endInstruction, endIndex, endsAtBodyEnd));
        }

        return envelopes;
    }

    private static int HandlerOpenIndex(ExceptionHandler handler, Dictionary<Instruction, int> indexOf) {
        bool isFilter = handler.HandlerType == ExceptionHandlerType.Filter;
        Instruction openPosition = isFilter ? handler.FilterStart : handler.HandlerStart;
        return IndexOf(openPosition, indexOf, handler, isFilter ? "FilterStart" : "HandlerStart");
    }

    private static int IndexOf(Instruction boundary, Dictionary<Instruction, int> indexOf, ExceptionHandler handler, string role) {
        if (indexOf.TryGetValue(boundary, out int at)) {
            return at;
        }

        throw new ConcordEmitException(
            CodeConversionFailed,
            $"A {handler.HandlerType.ToString().ToLowerInvariant()} handler's {role} names a '{boundary.OpCode.Name}' instruction that is not in the body being converted. " +
            $"Its boundaries are {DescribeBoundaries(handler, indexOf)}.");
    }

    private static string DescribeBoundaries(ExceptionHandler handler, Dictionary<Instruction, int> indexOf) {
        return "TryStart " + Describe(handler.TryStart, indexOf) +
            ", TryEnd " + Describe(handler.TryEnd, indexOf) +
            ", FilterStart " + Describe(handler.FilterStart, indexOf) +
            ", HandlerStart " + Describe(handler.HandlerStart, indexOf) +
            ", HandlerEnd " + Describe(handler.HandlerEnd, indexOf);
    }

    private static string Describe(Instruction? boundary, Dictionary<Instruction, int> indexOf) {
        if (boundary is null) {
            return "none";
        }

        return indexOf.TryGetValue(boundary, out int at)
            ? "at " + at
            : "missing '" + boundary.OpCode.Name + "'";
    }

    private static Label LabelOf(Instruction target, TranspilerContext context, string role) {
        if (context.LabelsByInstruction.TryGetValue(target, out Label label)) {
            return label;
        }

        throw new ConcordEmitException(
            CodeConversionFailed,
            $"{role} names a '{target.OpCode.Name}' instruction that is not in the body being converted.");
    }

    private static List<ExceptionEnvelope> LinkEnvelopes(List<ExceptionEnvelope> envelopes) {
        List<ExceptionEnvelope> roots = new List<ExceptionEnvelope>();

        foreach (ExceptionEnvelope envelope in envelopes) {
            ExceptionEnvelope? parent = TightestEnclosing(envelope, envelopes);
            if (parent is null) {
                roots.Add(envelope);
            } else {
                parent.Children.Add(envelope);
            }
        }

        foreach (ExceptionEnvelope envelope in envelopes) {
            envelope.Children.Sort((a, b) => a.TryStartIndex.CompareTo(b.TryStartIndex));
        }

        return roots;
    }

    // The tightest enclosing envelope is the parent: with several candidates containing this one,
    // the smallest span is the immediate parent and the rest are its own ancestors. Containment is
    // deliberately strict - an envelope sharing both boundaries with another is a sibling, not a
    // child, which is what a try/catch and its guarding try/finally look like when they coincide.
    private static ExceptionEnvelope? TightestEnclosing(ExceptionEnvelope envelope, List<ExceptionEnvelope> envelopes) {
        ExceptionEnvelope? parent = null;
        foreach (ExceptionEnvelope candidate in envelopes) {
            if (ReferenceEquals(candidate, envelope)) {
                continue;
            }

            bool contains = candidate.TryStartIndex <= envelope.TryStartIndex && envelope.EndIndex <= candidate.EndIndex
                && (candidate.TryStartIndex != envelope.TryStartIndex || candidate.EndIndex != envelope.EndIndex);
            if (!contains) {
                continue;
            }

            int candidateSpan = candidate.EndIndex - candidate.TryStartIndex;
            int parentSpan = parent is null ? int.MaxValue : parent.EndIndex - parent.TryStartIndex;
            if (parent is null || candidateSpan < parentSpan) {
                parent = candidate;
            }
        }

        return parent;
    }

    // Interleaves this envelope's own boundary markers with its children's, in the order a
    // transpiler author calling BeginExceptionBlock/BeginCatchBlock/.../EndExceptionBlock by hand
    // would have to call them: a child whose TryStart ties with one of this envelope's own boundary
    // positions opens strictly after that boundary is emitted (nothing can precede this envelope's
    // own open), and closes entirely - including its own EndExceptionBlock - before this envelope's
    // NEXT boundary is emitted, even when that next boundary lands on the exact same instruction
    // (a finally's handler always encloses its guarded catch, and the catch's close commonly lands
    // on the same instruction the finally's handler begins).
    private static void EmitEnvelope(ExceptionEnvelope envelope, Dictionary<Instruction, CodeInstruction> byInstruction, Dictionary<Instruction, int> indexOf, TranspilerContext context) {
        List<(int Index, Instruction Position, ExceptionBlock Block)> ownPoints = BuildOwnPoints(envelope, indexOf, context);
        int childCursor = 0;

        for (int i = 0; i < ownPoints.Count; i++) {
            (int pointIndex, Instruction position, ExceptionBlock block) = ownPoints[i];

            while (childCursor < envelope.Children.Count && envelope.Children[childCursor].TryStartIndex < pointIndex) {
                EmitEnvelope(envelope.Children[childCursor], byInstruction, indexOf, context);
                childCursor++;
            }

            byInstruction[position].blocks.Add(block);

            while (childCursor < envelope.Children.Count && envelope.Children[childCursor].TryStartIndex == pointIndex) {
                EmitEnvelope(envelope.Children[childCursor], byInstruction, indexOf, context);
                childCursor++;
            }
        }

        if (childCursor < envelope.Children.Count) {
            throw new ConcordEmitException(CodeConversionFailed, "An exception region's child region was not contained within its parent's own boundaries.");
        }
    }

    private static List<(int Index, Instruction Position, ExceptionBlock Block)> BuildOwnPoints(ExceptionEnvelope envelope, Dictionary<Instruction, int> indexOf, TranspilerContext context) {
        List<(int Index, Instruction Position, ExceptionBlock Block)> points = new List<(int, Instruction, ExceptionBlock)> {
            (envelope.TryStartIndex, envelope.TryStart, new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock)),
        };

        foreach (ExceptionHandler handler in envelope.Handlers) {
            if (handler.HandlerType == ExceptionHandlerType.Filter) {
                points.Add((IndexOf(handler.FilterStart, indexOf, handler, "FilterStart"), handler.FilterStart, new ExceptionBlock(ExceptionBlockType.BeginExceptFilterBlock)));
                points.Add((IndexOf(handler.HandlerStart, indexOf, handler, "HandlerStart"), handler.HandlerStart, new ExceptionBlock(ExceptionBlockType.BeginCatchBlock)));
            } else if (handler.HandlerType == ExceptionHandlerType.Fault) {
                points.Add((IndexOf(handler.HandlerStart, indexOf, handler, "HandlerStart"), handler.HandlerStart, new ExceptionBlock(ExceptionBlockType.BeginFaultBlock)));
            } else if (handler.HandlerType == ExceptionHandlerType.Finally) {
                points.Add((IndexOf(handler.HandlerStart, indexOf, handler, "HandlerStart"), handler.HandlerStart, new ExceptionBlock(ExceptionBlockType.BeginFinallyBlock)));
            } else {
                Type? catchType = handler.CatchType is null ? null : ResolveType(handler.CatchType);
                points.Add((IndexOf(handler.HandlerStart, indexOf, handler, "HandlerStart"), handler.HandlerStart, new ExceptionBlock(ExceptionBlockType.BeginCatchBlock, catchType)));
            }
        }

        ExceptionBlock endBlock = new ExceptionBlock(ExceptionBlockType.EndExceptionBlock);
        if (envelope.EndsAtBodyEnd) {
            context.EndOfBodyBlocks.Add(endBlock);
        }

        points.Add((envelope.EndIndex, envelope.EndInstruction, endBlock));
        return points;
    }

    private sealed class ExceptionEnvelope {
        internal ExceptionEnvelope(List<ExceptionHandler> handlers, Instruction tryStart, int tryStartIndex, Instruction endInstruction, int endIndex, bool endsAtBodyEnd) {
            Handlers = handlers;
            TryStart = tryStart;
            TryStartIndex = tryStartIndex;
            EndInstruction = endInstruction;
            EndIndex = endIndex;
            EndsAtBodyEnd = endsAtBodyEnd;
        }

        internal List<ExceptionHandler> Handlers { get; }

        internal Instruction TryStart { get; }

        internal int TryStartIndex { get; }

        internal Instruction EndInstruction { get; }

        internal int EndIndex { get; }

        internal bool EndsAtBodyEnd { get; }

        internal List<ExceptionEnvelope> Children { get; } = new List<ExceptionEnvelope>();
    }
}
