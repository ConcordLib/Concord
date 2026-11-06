using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using OpCode = System.Reflection.Emit.OpCode;

namespace Concord.Emit;

/// <summary>Reads an ECMA-335 IL stream and its header into a <see cref="ComposedBody" />.</summary>
public static class BodyDecoder {
    private const int CatchFlags = 0;

    private const int FilterFlags = 1;

    private const int FinallyFlags = 2;

    private static readonly Dictionary<ushort, OpCode> ByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(opcode => (ushort)opcode.Value);

    /// <summary>Decodes a body the host read back.</summary>
    /// <param name="method">The method the body belongs to, which supplies the generic context tokens resolve in.</param>
    /// <param name="body">The raw body and its header values.</param>
    /// <param name="refusal">Why the body could not be decoded, or <see langword="null" /> on success.</param>
    /// <returns>The decoded body, or <see langword="null" /> when <paramref name="refusal" /> says why not.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <c>null</c>.</exception>
    public static ComposedBody? Decode(MethodBase method, HostBody body, out string? refusal) {
        Guard.ThrowIfNull(method, nameof(method));
        Guard.ThrowIfNull(body, nameof(body));

        refusal = null;
        if (body.Il.Length == 0) {
            refusal = "the body carries no IL";
            return null;
        }

        (Type[]? typeArguments, Type[]? methodArguments) = LocalSignatureDecoder.GenericContext(method);

        IReadOnlyList<ComposedLocal>? locals = body.Locals ?? LocalSignatureDecoder.Decode(
            body.LocalSignature, method.Module, typeArguments, methodArguments, out refusal);
        if (locals is null) {
            return null;
        }

        Walk walk = new Walk(body.Il, method.Module, typeArguments, methodArguments);
        if (!walk.Decode(out refusal)) {
            return null;
        }

        return walk.Compose(method, body, locals, typeArguments, methodArguments, out refusal);
    }

    private sealed class Walk {
        private readonly byte[] il;
        private readonly Module module;
        private readonly Type[]? typeArguments;
        private readonly Type[]? methodArguments;
        private readonly List<int> offsets = [];
        private readonly List<OpCode> opcodes = [];
        private readonly List<object?> operands = [];
        private readonly List<int[]?> branches = [];

        internal Walk(byte[] il, Module module, Type[]? typeArguments, Type[]? methodArguments) {
            this.il = il;
            this.module = module;
            this.typeArguments = typeArguments;
            this.methodArguments = methodArguments;
        }

        internal bool Decode(out string? skipped) {
            skipped = null;
            int at = 0;
            while (at < il.Length) {
                int start = at;
                ushort value = il[at] == 0xfe ? (ushort)(0xfe00 | il[at + 1]) : il[at];
                at += il[start] == 0xfe ? 2 : 1;
                if (!ByValue.TryGetValue(value, out OpCode opcode)) {
                    skipped = $"opcode 0x{value:x4}";
                    return false;
                }

                object? operand = null;
                int[]? targets = null;
                switch (opcode.OperandType) {
                    case OperandType.InlineNone:
                        break;
                    case OperandType.ShortInlineBrTarget:
                        targets = [at + 1 + (sbyte)il[at]];
                        at += 1;
                        break;
                    case OperandType.InlineBrTarget:
                        targets = [at + 4 + BitConverter.ToInt32(il, at)];
                        at += 4;
                        break;
                    case OperandType.InlineSwitch: {
                        int cases = BitConverter.ToInt32(il, at);
                        int after = at + 4 + (cases * 4);
                        targets = new int[cases];
                        for (int index = 0; index < cases; index++) {
                            targets[index] = after + BitConverter.ToInt32(il, at + 4 + (index * 4));
                        }

                        at = after;
                        break;
                    }

                    case OperandType.ShortInlineI:
                        operand = opcode == OpCodes.Ldc_I4_S ? (sbyte)il[at] : il[at];
                        at += 1;
                        break;
                    case OperandType.InlineI:
                        operand = BitConverter.ToInt32(il, at);
                        at += 4;
                        break;
                    case OperandType.InlineI8:
                        operand = BitConverter.ToInt64(il, at);
                        at += 8;
                        break;
                    case OperandType.ShortInlineR:
                        operand = BitConverter.ToSingle(il, at);
                        at += 4;
                        break;
                    case OperandType.InlineR:
                        operand = BitConverter.ToDouble(il, at);
                        at += 8;
                        break;
                    case OperandType.ShortInlineVar:
                        operand = (int)il[at];
                        at += 1;
                        break;
                    case OperandType.InlineVar:
                        operand = (int)BitConverter.ToUInt16(il, at);
                        at += 2;
                        break;
                    case OperandType.InlineString:
                        try {
                            operand = module.ResolveString(BitConverter.ToInt32(il, at));
                        } catch (Exception unresolved) when (unresolved is not OutOfMemoryException) {
                            skipped = "ResolveString " + unresolved.GetType().Name;
                            return false;
                        }

                        at += 4;
                        break;
                    case OperandType.InlineType:
                    case OperandType.InlineField:
                    case OperandType.InlineMethod:
                    case OperandType.InlineTok: {
                        MemberInfo member;
                        try {
                            member = module.ResolveMember(BitConverter.ToInt32(il, at), typeArguments, methodArguments)!;
                        } catch (Exception unresolved) when (unresolved is not OutOfMemoryException) {
                            skipped = "ResolveMember " + unresolved.GetType().Name;
                            return false;
                        }

                        at += 4;
                        if (member is not (Type or FieldInfo or MethodBase)) {
                            skipped = "operand member " + member.GetType().Name;
                            return false;
                        }

                        operand = member;
                        break;
                    }

                    case OperandType.InlineSig: {
                        operand = Signature(BitConverter.ToInt32(il, at), start, opcode, out skipped);
                        if (operand is null) {
                            return false;
                        }

                        at += 4;
                        break;
                    }

                    default:
                        skipped = $"{opcode.Name} at IL_{start:x4} carries a {opcode.OperandType} operand";
                        return false;
                }

                offsets.Add(start);
                opcodes.Add(opcode);
                operands.Add(operand);
                branches.Add(targets);
            }

            return true;
        }

        internal ComposedBody? Compose(
            MethodBase method,
            HostBody body,
            IReadOnlyList<ComposedLocal> locals,
            Type[]? typeArguments,
            Type[]? methodArguments,
            out string? skipped) {
            skipped = null;
            Dictionary<int, int> indexOf = [];
            for (int index = 0; index < offsets.Count; index++) {
                indexOf[offsets[index]] = index;
            }

            indexOf[il.Length] = offsets.Count;

            ITranspilerContext context = TranspilerContextFactory.For(method);
            Dictionary<int, Label> marks = [];
            Label MarkAt(int offset) {
                if (!marks.TryGetValue(offset, out Label mark)) {
                    mark = context.DefineLabel();
                    marks[offset] = mark;
                }

                return mark;
            }

            for (int index = 0; index < opcodes.Count; index++) {
                int[]? targets = branches[index];
                if (targets is null) {
                    continue;
                }

                foreach (int target in targets) {
                    if (!indexOf.TryGetValue(target, out int resolved) || resolved == offsets.Count) {
                        skipped = "branch to " + target;
                        return null;
                    }
                }

                operands[index] = opcodes[index].OperandType == OperandType.InlineSwitch
                    ? targets.Select(MarkAt).ToArray()
                    : MarkAt(targets[0]);
            }

            ComposedInstruction[] instructions = new ComposedInstruction[opcodes.Count];
            for (int index = 0; index < opcodes.Count; index++) {
                CodeInstruction instruction = new CodeInstruction(opcodes[index], operands[index]);
                if (marks.TryGetValue(offsets[index], out Label mark)) {
                    instruction.labels.Add(mark);
                }

                instructions[index] = new ComposedInstruction(instruction, offsets[index], null);
            }

            List<ComposedClause> clauses = [];
            foreach (HostClause clause in body.Clauses) {
                if (!indexOf.TryGetValue(clause.TryOffset, out int tryStart)
                    || !indexOf.TryGetValue(clause.TryOffset + clause.TryLength, out int tryEnd)
                    || !indexOf.TryGetValue(clause.HandlerOffset, out int handlerStart)
                    || !indexOf.TryGetValue(clause.HandlerOffset + clause.HandlerLength, out int handlerEnd)) {
                    skipped = "clause boundary off an instruction";
                    return null;
                }

                int filterStart = -1;
                Type? caught = null;
                if (clause.Flags == CatchFlags) {
                    try {
                        caught = clause.CaughtType
                            ?? module.ResolveType(clause.ClassTokenOrFilterOffset, typeArguments, methodArguments);
                    } catch (Exception unresolved) when (unresolved is not OutOfMemoryException) {
                        skipped = "catch type " + unresolved.GetType().Name;
                        return null;
                    }

                    if (caught is null) {
                        skipped = "catch with no type";
                        return null;
                    }
                } else if (clause.Flags == FilterFlags
                    && !indexOf.TryGetValue(clause.ClassTokenOrFilterOffset, out filterStart)) {
                    skipped = "filter start off an instruction";
                    return null;
                }

                ClauseKind kind = clause.Flags switch {
                    CatchFlags => ClauseKind.Catch,
                    FilterFlags => ClauseKind.Filter,
                    FinallyFlags => ClauseKind.Finally,
                    _ => ClauseKind.Fault,
                };

                clauses.Add(new ComposedClause(kind, tryStart, tryEnd, handlerStart, handlerEnd, caught, filterStart));
            }

            return new ComposedBody(instructions, locals, clauses, body.InitLocals);
        }

        private static byte[] RetypedAsLocals(byte[] signature, int from, int count) {
            BlobBuilder builder = new BlobBuilder();
            builder.WriteByte(0x07);
            builder.WriteCompressedInteger(count + 1);
            builder.WriteBytes(signature, from, signature.Length - from);
            return builder.ToArray();
        }

        private CallSiteSignature? Signature(int token, int start, OpCode opcode, out string? skipped) {
            string where = $"{opcode.Name} at IL_{start:x4}";
            byte[] blob;
            try {
                blob = module.ResolveSignature(token);
            } catch (Exception unresolved) when (unresolved is not OutOfMemoryException) {
                skipped = $"{where} names a standalone signature that will not resolve: " +
                    unresolved.GetType().Name;
                return null;
            }

            if (blob.Length < 2) {
                skipped = $"{where} names a standalone signature of {blob.Length} bytes";
                return null;
            }

            byte header = blob[0];
            SignatureCallingConvention convention = (SignatureCallingConvention)(header & 0x0f);
            if (convention > SignatureCallingConvention.FastCall || (header & 0x40) != 0) {
                skipped = $"{where} calls through a standalone signature Concord does not re-encode: " +
                    $"calling convention 0x{header & 0x0f:x2}" +
                    ((header & 0x40) != 0 ? " with an explicit this" : string.Empty);
                return null;
            }

            int at = 1;
            int count;
            try {
                count = LocalSignatureDecoder.CompressedInteger(blob, ref at);
            } catch (IndexOutOfRangeException) {
                count = -1;
            }

            if (count < 0 || at > blob.Length) {
                skipped = $"{where} names a standalone signature whose parameter count is malformed";
                return null;
            }

            ComposedLocal[]? parts = LocalSignatureDecoder.Decode(
                RetypedAsLocals(blob, at, count), module, typeArguments, methodArguments, out string? refusal);
            if (parts is null || parts.Length != count + 1) {
                skipped = $"{where} names a standalone signature Concord could not read: " +
                    (refusal ?? $"it decoded to {parts?.Length ?? 0} of {count + 1} types");
                return null;
            }

            Type[] parameters = new Type[count];
            for (int index = 0; index < count; index++) {
                parameters[index] = parts[index + 1].Type;
            }

            skipped = null;
            return new CallSiteSignature(parts[0].Type, parameters, convention, (header & 0x20) != 0, false);
        }
    }
}
