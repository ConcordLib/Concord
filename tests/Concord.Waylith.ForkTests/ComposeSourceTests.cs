using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Concord.Waylith.ForkTests;

public static class Composed {
    public static int First;

    public static int Second;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Value() {
        return 1;
    }

    public static void CountFirst() {
        First++;
    }

    public static void CountSecond() {
        Second++;
    }
}

[Collection(ForkFieldInjection.ApplyCollection)]
public sealed class ComposeSourceTests : IDisposable {
    private const string NoFork = "No body-replacement runtime here, so nothing is composed in place.";

    private static readonly MethodInfo Target = typeof(Composed).GetMethod(nameof(Composed.Value))!;

    private readonly Action<string> logged = WaylithClrBackend.Log;

    private readonly List<string> lines = [];

    private bool published;

    public static bool Unavailable => ForkRuntime.Unavailable || !ForkRuntime.HostRead;

    public void Dispose() {
        WaylithClrBackend.Log = logged;
        if (published) {
            ForkRuntime.Restore(Target);
        }
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void ASecondPublishComposesOntoTheOriginalBodyNotTheFirstComposition() {
        WaylithClrBackend.Log = line => lines.Add(line);

        IPatchHandle first = ForkRuntime.Apply(Target, typeof(Composed), nameof(Composed.CountFirst), At.Tail);
        published = true;
        IPatchHandle second = ForkRuntime.Apply(Target, typeof(Composed), nameof(Composed.CountSecond), At.Tail);

        Assert.True(
            first.IsApplied && second.IsApplied,
            "one of the two patches reports IsApplied false, so the target does not carry both injections: " +
            string.Join(" | ", lines));

        Composed.First = 0;
        Composed.Second = 0;

        Assert.Equal(1, Composed.Value());

        Assert.True(
            Composed.First == 1 && Composed.Second == 1,
            $"one call to the twice-published target ran the first injection {Composed.First} time(s) and the " +
            $"second {Composed.Second} time(s), and each should run once. WrapperComposer.ComposeOnce reads the " +
            "target through BodyReader.Open, which on this runtime returns the body Concord already published, " +
            "so the second publish composes onto the first composition instead of onto the raw IL. At.Transpiler's " +
            "docblock states the invariant this breaks: Concord recomposes a target from its raw IL on every " +
            "patch and every unpatch of that target.");
    }
}
