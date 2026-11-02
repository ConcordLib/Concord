using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Detour;
using Xunit;

namespace Concord.Host.Adapter.ForkTests;

public static class Composed {
    public static int First;

    public static int Second;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Value() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Stolen() {
        return 1;
    }

    public static void CountFirst() {
        First++;
    }

    public static void CountSecond() {
        Second++;
    }
}

public sealed class RefusingBackend : IDetourBackend {
    public List<MethodBase> HandedOff { get; } = [];

    public IDetourHandle Apply(MethodBase original, MethodInfo replacement) {
        HandedOff.Add(original);
        throw new InvalidOperationException("the publisher conceded the target to the fallback");
    }

    public IDetourHandle ApplyComposed(MethodBase target, IReadOnlyList<Injection> added) {
        HandedOff.Add(target);
        throw new InvalidOperationException("the publisher conceded the target to the fallback");
    }
}

public sealed class ComposeSourceTests : IDisposable {
    private const string NoFork = "No body-replacement runtime here, so nothing is composed in place.";

    private static readonly MethodInfo Target = typeof(Composed).GetMethod(nameof(Composed.Value))!;

    private static readonly FieldInfo Recorded =
        typeof(PatchBuilder).GetField("injections", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private readonly Action<string> logged = WaylithDetourBackend.Log;

    private readonly List<string> lines = [];

    private bool published;

    private bool stolenWritten;

    public static bool Unavailable => ForkRuntime.Unavailable || !ForkRuntime.HostRead;

    public void Dispose() {
        WaylithDetourBackend.Log = logged;
        if (published) {
            ForkBodyWriter.Shared.Restore(Target);
        }

        if (stolenWritten) {
            ForkBodyWriter.Shared.Restore(typeof(Composed).GetMethod(nameof(Composed.Stolen))!);
        }
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void ASecondPublishComposesOntoTheOriginalBodyNotTheFirstComposition() {
        RefusingBackend fallback = new RefusingBackend();
        ComposedBodyPublisher publisher = new ComposedBodyPublisher(fallback);
        Injection first = Only(nameof(Composed.CountFirst));
        Injection second = Only(nameof(Composed.CountSecond));
        WaylithDetourBackend.Log = line => lines.Add(line);

        publisher.PublishComposed(Target, [first]);
        published = true;
        publisher.PublishComposed(Target, [first, second]);

        Assert.True(fallback.HandedOff.Count == 0, "the publisher conceded the target: " + string.Join(" | ", lines));

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

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void TheRegistrySeesSomeoneElseReplacingABodyConcordPublished() {
        MethodInfo stolen = typeof(Composed).GetMethod(nameof(Composed.Stolen))!;
        RefusingBackend fallback = new RefusingBackend();
        ComposedBodyPublisher publisher = new ComposedBodyPublisher(fallback);
        WaylithDetourBackend.Log = line => lines.Add(line);

        Assert.Equal(InstalledBody.Unknown, WaylithDetourBackend.Writers.Installed(stolen));

        publisher.PublishComposed(stolen, [Only(nameof(Composed.CountFirst), stolen)]);
        stolenWritten = true;
        Assert.True(fallback.HandedOff.Count == 0, "the publisher conceded the target: " + string.Join(" | ", lines));
        Assert.Equal(InstalledBody.Concords, WaylithDetourBackend.Writers.Installed(stolen));

        ForkRuntime.Write(stolen, 5);

        InstalledBody verdict = WaylithDetourBackend.Writers.Installed(stolen);

        Assert.True(
            verdict == InstalledBody.Replaced,
            $"someone wrote a body over the one Concord published and the registry reports {verdict}, so a clean " +
            "report still says nothing about whether Concord's body is the one installed.");
    }

    private static Injection Only(string injectionMethodName) {
        return Only(injectionMethodName, Target);
    }

    private static Injection Only(string injectionMethodName, MethodInfo target) {
        PatchBuilder builder = Patcher.For(target)
            .Tail(typeof(Composed).GetMethod(injectionMethodName)!);

        return ((List<Injection>)Recorded.GetValue(builder)!)[^1];
    }
}
