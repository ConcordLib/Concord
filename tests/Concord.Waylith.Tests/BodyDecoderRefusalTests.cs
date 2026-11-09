using System.Reflection;
using Xunit;

namespace Concord.Waylith.Tests;

public class BodyDecoderRefusalTests {
    private const byte LdcI4One = 0x17;

    private const byte Calli = 0x29;

    private const byte Ret = 0x2a;

    private static readonly MethodInfo Any =
        typeof(BodyDecoderRefusalTests).GetMethod(nameof(Subject), BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public void ASimpleBodyDecodesSoTheRefusalFactsBelowMeasureTheOperandAndNotTheHarness() {
        ComposedBody? decoded = BodyDecoder.Decode(Any, Body([LdcI4One, Ret]), out string? refusal);

        Assert.Null(refusal);
        Assert.NotNull(decoded);
        Assert.Equal(2, decoded.Instructions.Count);
    }

    [Fact]
    public void ACalliWithAnUnsupportedCallingConventionIsRefusedByNameAndOffset() {
        ComposedBody? decoded = BodyDecoder.Decode(Any, Body([LdcI4One, Calli, 1, 0, 0, 0x11, Ret]), out string? refusal);

        Assert.Null(decoded);
        Assert.NotNull(refusal);
        Assert.Contains("calli", refusal, StringComparison.Ordinal);
        Assert.Contains("IL_0001", refusal, StringComparison.Ordinal);
        Assert.Contains("standalone signature", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("InlineSig", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyBodyIsRefusedRatherThanDecodedAsZeroInstructions() {
        ComposedBody? decoded = BodyDecoder.Decode(Any, Body([]), out string? refusal);

        Assert.Null(decoded);
        Assert.NotNull(refusal);
    }

    private static HostBody Body(byte[] il) {
        return new HostBody(il, [], 0, 8, false, []);
    }

    private static int Subject() {
        return 1;
    }
}
