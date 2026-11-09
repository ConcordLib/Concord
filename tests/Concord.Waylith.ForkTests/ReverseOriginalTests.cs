using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Concord.Waylith.ForkTests;

public static class Declaring {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Reads() {
        return 1;
    }
}

[Collection(ForkFieldInjection.ApplyCollection)]
public sealed class ReverseOriginalTests {
    private const string NoFork = "No host body read here, so an installed body cannot differ from the declared one.";

    private static readonly MethodInfo Target = typeof(Declaring).GetMethod(nameof(Declaring.Reads))!;

    public static bool Unavailable => ForkRuntime.Unavailable || !ForkRuntime.HostRead;

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void ViewingAnAlreadyViewedMethodHandsBackTheSameView() {
        MethodBase once = OriginalBody.AsDeclared(Target);
        MethodBase twice = OriginalBody.AsDeclared(once);

        Assert.True(
            !ReferenceEquals(once, Target) && ReferenceEquals(twice, once),
            $"AsDeclared wrapped the target into {(ReferenceEquals(once, Target) ? "itself" : "a view")} and then " +
            $"wrapped that into {(ReferenceEquals(twice, once) ? "the same view" : "a second view")}. The hook " +
            "re-invokes the method it is injected into, so it asks for the view of a view. Wrapping twice sends a " +
            "non-runtime MethodInfo to the host's body read, which the runtime refuses outright.");
    }
}
