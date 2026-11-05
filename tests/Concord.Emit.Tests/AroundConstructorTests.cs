using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public sealed class AroundCtorTarget {
    public int Seed;

    public AroundCtorTarget(int seed) {
        Seed = seed;
        AroundCtorLog.Entries.Add("body:" + seed);
    }
}

public static class AroundCtorLog {
    public static List<string> Entries = new List<string>();
}

public static class AroundCtorInjectionMethods {
    public static void WrapZeroInvoke(int seed, VoidOperation<int> original) {
        AroundCtorLog.Entries.Add("skip");
    }

    public static void WrapLoopInvoke(int seed, VoidOperation<int> original) {
        for (int i = 0; i < 2; i++) {
            original.Invoke(seed + i);
        }
    }
}

public sealed class AroundCctorTarget {
    public static int Value = 1;
}

public static class AroundCctorInjectionMethods {
    public static void Wrap(Operation original) {
        original.Invoke();
    }
}

public sealed class AroundConstructorTests {
    [Fact]
    public void Compose_Constructor_ZeroInvokeSites_ThrowsCONC112() {
        ConstructorInfo ctor = typeof(AroundCtorTarget).GetConstructor([typeof(int)])!;
        MethodBase injectionMethod = typeof(AroundCtorInjectionMethods).GetMethod(nameof(AroundCtorInjectionMethods.WrapZeroInvoke))!;
        Injection around = new Injection(injectionMethod, new InjectAt.Around(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            WrapperComposer.Compose(ctor, [around]));

        Assert.Equal("CONC112", ex.Code);
    }

    [Fact]
    public void Compose_Constructor_InvokeInLoop_ThrowsCONC113() {
        ConstructorInfo ctor = typeof(AroundCtorTarget).GetConstructor([typeof(int)])!;
        MethodBase injectionMethod = typeof(AroundCtorInjectionMethods).GetMethod(nameof(AroundCtorInjectionMethods.WrapLoopInvoke))!;
        Injection around = new Injection(injectionMethod, new InjectAt.Around(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            WrapperComposer.Compose(ctor, [around]));

        Assert.Equal("CONC113", ex.Code);
    }

    [Fact]
    public void Compose_StaticConstructor_ThrowsCONC114() {
        ConstructorInfo cctor = typeof(AroundCctorTarget).TypeInitializer!;
        MethodBase injectionMethod = typeof(AroundCctorInjectionMethods).GetMethod(nameof(AroundCctorInjectionMethods.Wrap))!;
        Injection around = new Injection(injectionMethod, new InjectAt.Around(), "test", 0);

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            WrapperComposer.Compose(cctor, [around]));

        Assert.Equal("CONC114", ex.Code);
    }
}
