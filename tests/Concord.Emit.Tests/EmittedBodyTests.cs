using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace Concord.Emit.Tests;

public static class EmittedBodyTargets {
    public static int Observed;

    public static int Add(int a, int b) {
        return a + b;
    }

    public static string Caught(int divisor) {
        try {
            return (10 / divisor).ToString();
        } catch (DivideByZeroException) {
            return "handled";
        }
    }

    public static void Watch(ControlHandle ch, int a, int b) {
        _ = ch;
        Observed = a + b;
    }
}

public sealed class EmittedBodyTests {
    [Fact]
    public void AReversePatchOntoAnOwnedBodyReturnsTheOriginalsValue() {
        MethodBase target = typeof(EmittedBodyTargets).GetMethod(nameof(EmittedBodyTargets.Add))!;
        Func<MethodBase, bool>? owned = ReversePatchFactory.OwnsBody;
        ReversePatchFactory.OwnsBody = _ => true;
        try {
            Func<int, int, int> reverse =
                (Func<int, int, int>)ReversePatchFactory.Bind(target, typeof(Func<int, int, int>));

            Assert.Equal(5, reverse(2, 3));
        } finally {
            ReversePatchFactory.OwnsBody = owned;
        }
    }

    [Fact]
    public void ACallableOriginalWithACatchRegionRunsItsHandler() {
        MethodBase target = typeof(EmittedBodyTargets).GetMethod(nameof(EmittedBodyTargets.Caught))!;

        MethodInfo clone = OriginalBody.Clone(target);

        Assert.Equal("5", clone.Invoke(null, [2]));
        Assert.Equal("handled", clone.Invoke(null, [0]));
    }

    [Fact]
    public void AComposedWrapperWithOneInjectionRunsThroughTheWrapper() {
        MethodBase target = typeof(EmittedBodyTargets).GetMethod(nameof(EmittedBodyTargets.Add))!;
        Injection head = new Injection(
            typeof(EmittedBodyTargets).GetMethod(nameof(EmittedBodyTargets.Watch))!,
            new InjectAt.Head(),
            "emitted-body-head",
            0);
        EmittedBodyTargets.Observed = 0;

        ComposeResult composed = WrapperComposer.Compose(target, [head]);

        Assert.Empty(composed.Rejected);
        Assert.Equal(5, composed.Wrapper.Invoke(null, [2, 3]));
        Assert.Equal(5, EmittedBodyTargets.Observed);
    }

    [Fact]
    public void TheEncodersCodeLocalsAndExceptionOutputsRoundTripThroughTheDecoder() {
        Label exit = new Label(1);
        LocalRef slot = new LocalRef(0, typeof(int));
        CodeInstruction[] stream = [
            Step(OpCodes.Ldc_I4, 0),
            Step(OpCodes.Stloc, slot),
            Step(OpCodes.Ldc_I4, 7),
            Step(OpCodes.Stloc, slot),
            Step(OpCodes.Leave, exit),
            Step(OpCodes.Ldc_I4, 1),
            Step(OpCodes.Pop),
            Step(OpCodes.Endfinally),
            Step(OpCodes.Ldloc, slot, exit),
            Step(OpCodes.Ret),
        ];
        ComposedClause guarded = new ComposedClause(ClauseKind.Finally, 2, 5, 5, 8, null, -1);
        ComposedBody body = new ComposedBody(
            Wrap(stream),
            [new ComposedLocal(typeof(int), false)],
            [guarded],
            true);

        EncodedBody encoded = BodyEncoder.Encode(body, new NoMemberTokens(), new FixedMaxStack(2));

        Assert.NotEmpty(encoded.LocalSignature);
        Assert.NotEmpty(encoded.ExceptionSection);
        HostBody raw = new HostBody(
            encoded.Il,
            encoded.LocalSignature,
            0,
            encoded.MaxStack,
            encoded.InitLocals,
            [
                new HostClause(
                    2,
                    encoded.InstructionOffsets[2],
                    encoded.InstructionOffsets[5] - encoded.InstructionOffsets[2],
                    encoded.InstructionOffsets[5],
                    encoded.InstructionOffsets[8] - encoded.InstructionOffsets[5],
                    0),
            ]);

        ComposedBody? decoded = BodyDecoder.Decode(
            typeof(EmittedBodyTargets).GetMethod(nameof(EmittedBodyTargets.Add))!, raw, out string? refusal);

        Assert.Null(refusal);
        Assert.NotNull(decoded);
        Assert.Equal(
            Array.ConvertAll(stream, instruction => instruction.opcode.Name),
            decoded.Instructions.Select(instruction => instruction.Instruction.opcode.Name).ToArray());
        Assert.True(decoded.InitLocals);
        Assert.Equal(typeof(int), Assert.Single(decoded.Locals).Type);
        ComposedClause clause = Assert.Single(decoded.Clauses);
        Assert.Equal(ClauseKind.Finally, clause.Kind);
        Assert.Equal(guarded.TryStart, clause.TryStart);
        Assert.Equal(guarded.TryEnd, clause.TryEnd);
        Assert.Equal(guarded.HandlerStart, clause.HandlerStart);
        Assert.Equal(guarded.HandlerEnd, clause.HandlerEnd);
    }

    [Fact]
    public void AnEncodedBodyWithAFinallyRegionHandsBackItsIlAndExceptionSectionByteForByte() {
        Label exit = new Label(1);
        CodeInstruction[] stream = [
            Step(OpCodes.Nop),
            Step(OpCodes.Leave, exit),
            Step(OpCodes.Nop),
            Step(OpCodes.Endfinally),
            Step(OpCodes.Ret, null, exit),
        ];
        ComposedBody body = new ComposedBody(
            Wrap(stream),
            [],
            [new ComposedClause(ClauseKind.Finally, 0, 2, 2, 4, null, -1)],
            false);

        EncodedBody encoded = BodyEncoder.Encode(body, new NoMemberTokens(), new FixedMaxStack(1));

        byte[] bytes = encoded.Bytes;
        int codeSize = BitConverter.ToInt32(bytes, 4);
        int section = (12 + codeSize + 3) & ~3;
        Assert.Equal(encoded.CodeSize, codeSize);
        Assert.Equal(bytes.Skip(12).Take(codeSize).ToArray(), encoded.Il);
        Assert.Equal(bytes.Skip(section).ToArray(), encoded.ExceptionSection);
        Assert.NotEmpty(encoded.ExceptionSection);
    }

    private static CodeInstruction Step(OpCode opcode, object? operand = null, params Label[] labels) {
        return new CodeInstruction(opcode, operand).WithLabels(labels);
    }

    private static IReadOnlyList<ComposedInstruction> Wrap(IReadOnlyList<CodeInstruction> stream) {
        ComposedInstruction[] wrapped = new ComposedInstruction[stream.Count];
        for (int index = 0; index < stream.Count; index++) {
            wrapped[index] = new ComposedInstruction(stream[index], null, null);
        }

        return wrapped;
    }

    private sealed class NoMemberTokens : ITokenSource {
        public int TokenFor(Type type) {
            throw new InvalidOperationException($"the body needs no type token, so '{type}' is a surprise");
        }

        public int TokenFor(FieldInfo field) {
            throw new InvalidOperationException($"the body needs no field token, so '{field}' is a surprise");
        }

        public int TokenFor(MethodBase method) {
            throw new InvalidOperationException($"the body needs no method token, so '{method}' is a surprise");
        }

        public int TokenFor(string literal) {
            throw new InvalidOperationException($"the body needs no string token, so '{literal}' is a surprise");
        }

        public int TokenForLocals(byte[] signature) {
            return 0x11000001;
        }
    }

    private sealed class FixedMaxStack : IMaxStackSource {
        private readonly int value;

        internal FixedMaxStack(int value) {
            this.value = value;
        }

        public int MaxStackFor(ComposedBody body) {
            return value;
        }
    }
}
