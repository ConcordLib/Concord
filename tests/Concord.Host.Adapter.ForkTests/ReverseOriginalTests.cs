using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Xunit;

namespace Concord.Host.Adapter.ForkTests;

public static class Declaring {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Reads() {
        return 1;
    }

    public static int Counted { get; private set; }

    public static void Count() {
        Counted++;
    }

    public static void Forget() {
        Counted = 0;
    }
}

public static class ViewStandin {
    public static int Reads() {
        throw new NotImplementedException();
    }
}

public static class RawStandin {
    public static int Reads() {
        throw new NotImplementedException();
    }
}

[Collection(ForkFieldInjection.ApplyCollection)]
public sealed class ReverseOriginalTests : IDisposable {
    private const string NoFork = "No host body read here, so an installed body cannot differ from the declared one.";

    private static readonly MethodInfo Target = typeof(Declaring).GetMethod(nameof(Declaring.Reads))!;

    private static readonly MethodInfo Injection = typeof(Declaring).GetMethod(nameof(Declaring.Count))!;

    private readonly Harmony harmony = new Harmony("concord.fork.reverse-original");

    private bool published;

    public static bool Unavailable => ForkRuntime.Unavailable || !ForkRuntime.HostRead;

    public void Dispose() {
        harmony.UnpatchAll(harmony.Id);
        if (published) {
            ForkRuntime.Restore(Target);
            WaylithDetourBackend.Writers.Release(Target);
        }

        Declaring.Forget();
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void AnOriginalReversePatchOfTheDeclaredViewRunsTheDeclaredBody() {
        Publish();

        MethodInfo written = harmony
            .CreateReversePatcher(
                OriginalBody.AsDeclared(Target),
                new HarmonyMethod(typeof(ViewStandin).GetMethod(nameof(ViewStandin.Reads))!))
            .Patch(HarmonyReversePatchType.Original);

        int before = Declaring.Counted;
        int returned = (int)written.Invoke(null, null)!;
        int after = Declaring.Counted;

        Assert.True(
            returned == 1 && after == before,
            $"an Original reverse patch of the declared view returned {returned} and moved the injection count " +
            $"from {before} to {after}. Harmony reads the stream through MethodBase.GetMethodBody(), so the view's " +
            "whole job is to answer that call with the declared body. A count that moved means Harmony copied " +
            "Concord's wrapper IL into the stand-in, so a mod reaching for vanilla IL gets patched IL instead.");
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void AnOriginalReversePatchOfTheMethodItselfStillRunsTheInstalledBody() {
        Publish();

        MethodInfo written = harmony
            .CreateReversePatcher(
                Target,
                new HarmonyMethod(typeof(RawStandin).GetMethod(nameof(RawStandin.Reads))!))
            .Patch(HarmonyReversePatchType.Original);

        int before = Declaring.Counted;
        written.Invoke(null, null);
        int after = Declaring.Counted;

        Assert.True(
            after == before + 1,
            $"an Original reverse patch of the method itself moved the injection count from {before} to {after}, " +
            "wanted one step. This runtime reports the installed body from GetMethodBody(), which is the only " +
            "reason the declared view exists. If this stops moving, the runtime started telling the truth and the " +
            "view can go.");
    }

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

    private void Publish() {
        RefusingBackend fallback = new RefusingBackend();
        ComposedBodyPublisher publisher = new ComposedBodyPublisher(fallback, ForkBodyWriter.Shared);

        publisher.PublishComposed(Target, [new Injection(Injection, new InjectAt.Head(), "reverse-original", 0)]);
        published = true;

        Assert.True(fallback.HandedOff.Count == 0, "the publisher conceded the target, so nothing was published");
        Assert.Equal(1, Declaring.Reads());
        Assert.True(Declaring.Counted == 1, $"the patched target ran its injection {Declaring.Counted} time(s), wanted 1");
    }
}
