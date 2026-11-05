using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Concord.Host.Adapter.ForkTests;

public static class StandInVictims {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Staged() {
        return 11;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Detached() {
        return 21;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Refused() {
        return 31;
    }
}

[Collection(ForkFieldInjection.ApplyCollection)]
public sealed class PipelineStandInTests : IDisposable {
    private const string NoPipeline =
        "No body-replacement runtime here, so a method body transformer has nothing to register with.";

    private readonly List<StandInTransformer> registered = [];

    public static bool Unavailable => ForkRuntime.Unavailable;

    public void Dispose() {
        foreach (StandInTransformer transformer in registered) {
            transformer.Unregister();
        }

        registered.Clear();
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void AStagedStandInRewritesTheVictimsBody() {
        StandInTransformer standIn = Registered(0, _ => ForkRuntime.Returning(99));
        MethodInfo target = Victim(nameof(StandInVictims.Staged));
        RuntimeMethodHandle victim = StandInTransformer.HandleOf(target);

        Exception? failure = standIn.Rebuild(target);

        Assert.Null(failure);
        Assert.True(
            standIn.Seen.Contains(victim),
            $"the stand-in saw {standIn.Seen.Count} handles and the victim's was not among them, so the pipeline " +
            "never ran this stage and the value below proves nothing about a transform.");
        Assert.Equal(99, StandInVictims.Staged());
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void UnregisteringAStandInPutsTheVictimBackOnItsDefaultBody() {
        StandInTransformer standIn = Registered(0, _ => ForkRuntime.Returning(98));
        MethodInfo target = Victim(nameof(StandInVictims.Detached));
        RuntimeMethodHandle victim = StandInTransformer.HandleOf(target);

        standIn.Rebuild(target);

        Assert.True(
            standIn.Seen.Contains(victim),
            $"the stand-in saw {standIn.Seen.Count} handles and the victim's was not among them, so nothing was " +
            "ever staged and the revert below would pass on a victim that was never rewritten.");
        Assert.Equal(98, StandInVictims.Detached());

        standIn.Unregister();
        registered.Remove(standIn);

        Assert.Equal(21, StandInVictims.Detached());
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void AThrowingStandInLeavesTheVictimOnItsDefaultBody() {
        StandInTransformer standIn = Registered(0, _ => ForkRuntime.Returning(97));
        MethodInfo target = Victim(nameof(StandInVictims.Refused));
        RuntimeMethodHandle victim = StandInTransformer.HandleOf(target);
        standIn.Throws = true;

        Exception? failure = standIn.Rebuild(target);

        Assert.True(
            standIn.Seen.Contains(victim),
            $"the stand-in saw {standIn.Seen.Count} handles and the victim's was not among them, so the stage never " +
            "ran and the exception below did not come from a transform.");
        Assert.NotNull(failure);
        Assert.Equal(StandInTransformer.Refusal, failure!.Message);
        Assert.Equal(31, StandInVictims.Refused());
    }

    private static MethodInfo Victim(string name) {
        return typeof(StandInVictims).GetMethod(name)!;
    }

    private StandInTransformer Registered(int priority, Func<byte[], byte[]?> rewrite) {
        StandInTransformer standIn = new StandInTransformer(priority, rewrite);
        standIn.Register();
        registered.Add(standIn);
        return standIn;
    }
}

internal sealed class StandInTransformer : WaylithClr.MethodBodyTransformer {
    internal const string Refusal = "the stand-in refused this body";

    private readonly Func<byte[], byte[]?> rewrite;

    internal StandInTransformer(int priority, Func<byte[], byte[]?> rewrite) : base(priority) {
        this.rewrite = rewrite;
    }

    internal List<RuntimeMethodHandle> Seen { get; } = [];

    internal List<byte[]> SeenIl { get; } = [];

    internal bool Throws { get; set; }

    internal static RuntimeMethodHandle HandleOf(MethodBase target) {
        return WaylithClr.MethodBodyUpdater.GetDefinitionHandle(target);
    }

    internal Exception? Rebuild(MethodBase target) {
        return Rebuild(HandleOf(target));
    }

    protected override bool TryTransform(
        RuntimeMethodHandle method,
        in WaylithClr.MethodBodyView body,
        ReadOnlySpan<WaylithClr.MethodBodyClause> clauses,
        WaylithClr.MethodBodyBuilder result) {
        Seen.Add(method);
        SeenIl.Add(body.IL.ToArray());
        if (Throws) {
            throw new InvalidOperationException(Refusal);
        }

        if (rewrite(body.IL.ToArray()) is not byte[] written) {
            return false;
        }

        result.Write(written);
        return true;
    }
}
