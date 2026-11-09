using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Backend;
using Concord.Emit;
using Xunit;

namespace Concord.Orchestration.Tests;

public class ProbeBox<T> {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public string Ping() {
        return "orig";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public string Describe() {
        return typeof(T).Name;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string SharedStaticPing() {
        return "orig";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public string Identity<TValue>() {
        return "orig";
    }
}

public struct ProbeStructBox<T> {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public string Ping() {
        return "orig";
    }
}

public static class ProbeInjection {
    public static void AfterPing(ControlHandle<string> ch) {
        ch.ReturnValue = ch.ReturnValue + "+p";
    }
}

public class SharedGenericProbeTests {
    [Fact]
    public void ContextTouchingBody_IsNotGuardable_AndStaysRejected() {
        MethodBase target = typeof(ProbeBox<string>).GetMethod(nameof(ProbeBox<string>.Describe))!;
        Injection[] ordered = [new Injection(typeof(ProbeInjection).GetMethod(nameof(ProbeInjection.AfterPing))!, new InjectAt.Tail(), "probe", 0)];

        Assert.False(WrapperComposer.IsSharedBodyGenericContextFree(target));
        Assert.False(WrapperComposer.CanGuardSharedInstantiation(target, ordered));
        Assert.Throws<ConcordEmitException>(() => WrapperComposer.RejectSharedGenericInstantiation(target, ordered));
    }

    [Fact]
    public void ContextFreeBody_IsGuardable() {
        if (!WrapperComposer.SharedGenericGuardSupported) {
            return;
        }

        MethodBase target = typeof(ProbeBox<string>).GetMethod(nameof(ProbeBox<string>.Ping))!;
        Injection[] ordered = [new Injection(typeof(ProbeInjection).GetMethod(nameof(ProbeInjection.AfterPing))!, new InjectAt.Tail(), "probe", 0)];

        Assert.True(WrapperComposer.IsSharedBodyGenericContextFree(target));
        Assert.True(WrapperComposer.CanGuardSharedInstantiation(target, ordered));
        WrapperComposer.RejectSharedGenericInstantiation(target, ordered);
    }

    [Fact]
    public void StaticMethodOnGenericType_IsNotGuardable() {
        MethodBase target = typeof(ProbeBox<string>).GetMethod(nameof(ProbeBox<string>.SharedStaticPing))!;
        Assert.False(WrapperComposer.CanGuardSharedInstantiation(target, Tail()));
    }

    [Fact]
    public void GenericMethod_IsNotGuardable() {
        MethodBase target = typeof(ProbeBox<string>).GetMethod(nameof(ProbeBox<string>.Identity))!.MakeGenericMethod(typeof(string));
        Assert.False(WrapperComposer.CanGuardSharedInstantiation(target, Tail()));
    }

    private static Injection[] Tail() {
        return [new Injection(typeof(ProbeInjection).GetMethod(nameof(ProbeInjection.AfterPing))!, new InjectAt.Tail(), "probe", 0)];
    }
}

public class SharedGenericTwoInstantiationTests {
    private static MethodInfo Ping<T>() {
        return typeof(ProbeBox<T>).GetMethod(nameof(ProbeBox<T>.Ping))!;
    }

    [Fact]
    public void SharedBodyKey_CollapsesReferenceTypesAndKeepsValueTypesApart() {
        if (!WrapperComposer.SharedGenericGuardSupported) {
            return;
        }

        MethodBase text = MethodIdentity.SharedBodyKey(Ping<string>());
        MethodBase version = MethodIdentity.SharedBodyKey(Ping<System.Version>());
        MethodBase number = MethodIdentity.SharedBodyKey(Ping<int>());

        Assert.Equal(text, version);
        Assert.NotEqual(text, number);
        Assert.Equal(typeof(ProbeBox<object>), text.DeclaringType);
    }

    [Fact]
    public void GenericStructReceiver_IsNotTreatedAsASharedBody() {
        MethodBase target = typeof(ProbeStructBox<string>).GetMethod(nameof(ProbeStructBox<string>.Ping))!;

        Assert.False(WrapperComposer.SharesGenericBody(target));
        Assert.Equal(target, MethodIdentity.SharedBodyKey(target));
    }
}
