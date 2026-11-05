using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Concord.Host.Adapter.ForkTests;

public sealed class Holder<T> {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Count(T held) {
        return held is null ? 0 : 1;
    }
}

public static class Counted {
    public static void Tail() {
    }
}

public sealed class SharedGenericVerdictTests : IDisposable {
    private const string NoFork = "No host body read here, so a published body cannot shift the verdict.";

    private static readonly FieldInfo Recorded =
        typeof(PatchBuilder).GetField("injections", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly MethodInfo Reference = typeof(Holder<string>).GetMethod(nameof(Holder<string>.Count))!;

    private bool published;

    public static bool Unavailable => ForkRuntime.Unavailable || !ForkRuntime.HostRead;

    public void Dispose() {
        if (published) {
            ForkRuntime.Restore(Reference);
        }
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void PublishingABodyDoesNotShiftTheVerdictOnWhetherAGenericBodyIsShared() {
        MethodInfo second = typeof(Holder<object>).GetMethod(nameof(Holder<object>.Count))!;
        bool before = Shared(Reference);

        ForkRuntime.Write(Reference, 0);
        published = true;

        bool after = Shared(second);

        Assert.True(
            before == after,
            $"'{Reference.DeclaringType}' answered shared={before} with no body installed, and " +
            $"'{second.DeclaringType}' answered shared={after} after a body was staged over " +
            $"'{Reference.DeclaringType}'. Both instantiations are reference types sharing one body, so the " +
            "answer must not depend on whether anything has written to it. IsSharedBodyGenericContextFree " +
            "compares the two bodies, and a read that reports the installed body instead of the declared one " +
            "makes that comparison answer from staged IL.");
    }

    private static bool Shared(MethodInfo target) {
        Injection[] added = [Only(target)];
        return !ReferenceEquals(WrapperComposer.TagRequestedInstantiation(target, added), added);
    }

    private static Injection Only(MethodInfo target) {
        PatchBuilder builder = Patcher.For(target).Tail(typeof(Counted).GetMethod(nameof(Counted.Tail))!);
        return ((List<Injection>)Recorded.GetValue(builder)!)[^1];
    }
}
