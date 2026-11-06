using System.Reflection;
using System.Reflection.Emit;
using Concord;
using Xunit;

namespace Concord.Emit.Tests;

public static class MonoEmitLocalsTargets {
    public static int Compute(int x) {
        return x + 1;
    }

    public static int Seed(int x) {
        return x;
    }
}

public static class MonoEmitLocalsInjectionMethods {
    public static int DoubleInnerResult(int x, Operation<int, int> original) {
        int inner = original.Invoke(x);
        return inner * 2;
    }

    public static IEnumerable<CodeInstruction> DeclareStoreAndLoadLocal(
        IEnumerable<CodeInstruction> instructions, ITranspilerContext context) {
        LocalRef scratch = context.DeclareLocal(typeof(int));
        List<CodeInstruction> output = new List<CodeInstruction>(instructions);
        int ret = output.FindLastIndex(i => i.opcode == OpCodes.Ret);

        output.Insert(ret, new CodeInstruction(OpCodes.Pop));
        output.Insert(ret + 1, new CodeInstruction(OpCodes.Ldarg_0));
        output.Insert(ret + 2, new CodeInstruction(OpCodes.Ldc_I4_1));
        output.Insert(ret + 3, new CodeInstruction(OpCodes.Add));
        output.Insert(ret + 4, new CodeInstruction(OpCodes.Stloc_S, scratch));
        output.Insert(ret + 5, new CodeInstruction(OpCodes.Ldloc_S, scratch));

        return output;
    }
}

public sealed class MonoEmitLocalsTests {
    [Fact]
    public void AroundWrapperWithLocal_RoundTripsInnerResultThroughSlot() {
        MethodBase target = typeof(MonoEmitLocalsTargets).GetMethod(nameof(MonoEmitLocalsTargets.Compute))!;
        MethodBase injectionMethod =
            typeof(MonoEmitLocalsInjectionMethods).GetMethod(nameof(MonoEmitLocalsInjectionMethods.DoubleInnerResult))!;
        Injection around = new Injection(injectionMethod, new InjectAt.Around(), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [around]);
        object? value = result.Wrapper.Invoke(null, [3]);

        Assert.Equal(8, value);
    }

    [Fact]
    public void TranspilerDeclaredLocal_RoundTripsComputedValueThroughSlot() {
        MethodBase target = typeof(MonoEmitLocalsTargets).GetMethod(nameof(MonoEmitLocalsTargets.Seed))!;
        MethodBase transpiler =
            typeof(MonoEmitLocalsInjectionMethods).GetMethod(nameof(MonoEmitLocalsInjectionMethods.DeclareStoreAndLoadLocal))!;
        Injection injection = new Injection(transpiler, new InjectAt.Transpiler(false), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [injection]);
        object? value = result.Wrapper.Invoke(null, [5]);

        Assert.Equal(6, value);
    }
}
