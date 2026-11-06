using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using Xunit;

namespace Concord.Emit.Tests;

public static class CalliTranspilerTargets {
    public static int Helper(int x) {
        return x * 2;
    }

    public static int Twice(int x) {
        return Helper(x);
    }
}

public static class CalliTranspilers {
    public static IEnumerable<CodeInstruction> CallHelperThroughAPointer(IEnumerable<CodeInstruction> instructions) {
        MethodInfo helper = typeof(CalliTranspilerTargets).GetMethod(nameof(CalliTranspilerTargets.Helper))!;
        List<CodeInstruction> output = new List<CodeInstruction>(instructions);
        int call = output.FindIndex(i => i.opcode == OpCodes.Call && Equals(i.operand, helper));

        output[call] = new CodeInstruction(
            OpCodes.Calli,
            new CallSiteSignature(typeof(int), [typeof(int)], SignatureCallingConvention.Default, false, false));
        output.Insert(call, new CodeInstruction(OpCodes.Ldftn, helper));

        return output;
    }
}

public sealed class CalliTranspilerTests {
    [Fact]
    public void ATranspilerCalliThroughAFunctionPointer_RunsTheCallee() {
        MethodBase target = typeof(CalliTranspilerTargets).GetMethod(nameof(CalliTranspilerTargets.Twice))!;
        MethodBase transpiler = typeof(CalliTranspilers).GetMethod(nameof(CalliTranspilers.CallHelperThroughAPointer))!;
        Injection injection = new Injection(transpiler, new InjectAt.Transpiler(false), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [injection]);
        object? value = result.Wrapper.Invoke(null, [21]);

        Assert.Equal(42, value);
    }
}
