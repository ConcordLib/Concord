using System;
using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace Concord.Emit.Tests;

public sealed class ScratchRefusalTests {
    [Fact]
    public void ARefusedOperandNamesTheOriginMethod() {
        MethodBase origin = typeof(ScratchRefusalTests)
            .GetMethod(nameof(Refusable), BindingFlags.NonPublic | BindingFlags.Static)!;
        ComposedBody body = new ComposedBody(
            [
                new ComposedInstruction(new CodeInstruction(OpCodes.Ldstr, 7), 0, null),
                new ComposedInstruction(new CodeInstruction(OpCodes.Ret), 5, null),
            ],
            [],
            [],
            false);

        ConcordEmitException refusal = Assert.Throws<ConcordEmitException>(
            () => ScratchMethod.Generate(body, origin.Name, typeof(string), Type.EmptyTypes, origin));

        Assert.Contains(nameof(Refusable), refusal.Message);
        Assert.Contains(typeof(ScratchRefusalTests).FullName!, refusal.Message);
    }

    private static string Refusable() {
        return string.Empty;
    }
}
