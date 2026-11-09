using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Emit;
using Xunit;

namespace Concord.Orchestration.Tests;

public class IsolationBox<T> {
    private readonly T value;

    public IsolationBox(T value) {
        this.value = value;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public T Get() {
        return value;
    }
}

public static class IsolationRegistry<T> {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string Describe() {
        return typeof(T).Name;
    }
}

public static class IsolationStringTailInjection {
    public static void AfterGet(ControlHandle<string> ch) {
        ch.ReturnValue = "patched";
    }
}

public static class IsolationIntTailInjection {
    public static void AfterGet(ControlHandle<int> ch) {
        ch.ReturnValue = 99;
    }
}

public class GenericInstantiationIsolationTests : IDisposable {
    private readonly RecordingDetourBackend backend = new RecordingDetourBackend();

    public GenericInstantiationIsolationTests() {
        Concord.Backend.PatchBackend.Current = backend;
    }

    public void Dispose() {
        Concord.Backend.PatchBackend.Reset();
    }

    [Fact]
    public void Tail_RefTypeInstanceInstantiation_IsRejected() {
        MethodInfo injectionMethod = typeof(IsolationStringTailInjection).GetMethod(nameof(IsolationStringTailInjection.AfterGet))!;

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            Patcher.For<IsolationBox<string>>(nameof(IsolationBox<string>.Get))
                .Tail(injectionMethod)
                .Apply());

        Assert.Equal("CONC061", ex.Code);
        Assert.Equal("hi", new IsolationBox<string>("hi").Get());
    }

    [Fact]
    public void Tail_RefTypeStaticInstantiation_IsRejected() {
        MethodInfo injectionMethod = typeof(IsolationStringTailInjection).GetMethod(nameof(IsolationStringTailInjection.AfterGet))!;

        ConcordEmitException ex = Assert.Throws<ConcordEmitException>(() =>
            Patcher.For(typeof(IsolationRegistry<string>), nameof(IsolationRegistry<string>.Describe))
                .Tail(injectionMethod)
                .Apply());

        Assert.Equal("CONC061", ex.Code);
        Assert.Equal("String", IsolationRegistry<string>.Describe());
        Assert.Equal("Version", IsolationRegistry<Version>.Describe());
    }

    [Fact]
    public void Tail_ValueTypeInstantiation_IsAllowed() {
        MethodInfo injectionMethod = typeof(IsolationIntTailInjection).GetMethod(nameof(IsolationIntTailInjection.AfterGet))!;

        IPatchHandle handle = Patcher.For<IsolationBox<int>>(nameof(IsolationBox<int>.Get))
            .Tail(injectionMethod)
            .Apply();
        try {
            Assert.Equal(
                typeof(IsolationBox<int>).GetMethod(nameof(IsolationBox<int>.Get)),
                Assert.Single(backend.Applied).Target);
        } finally {
            handle.Dispose();
        }
    }
}
