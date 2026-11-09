using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Concord.Waylith.ForkTests;

public static class Reversed {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Cloned() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Bound() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Untouched() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Guarded(int n) {
        int held = n;
        try {
            held = checked(held * 2);
        } catch (OverflowException) {
            held = -1;
        } finally {
            held += 1;
        }

        return held;
    }
}

public sealed class ReversePatchBodyTests : IDisposable {
    private const string NoFork = "No body-replacement runtime here, so nothing can be published.";

    private readonly List<MethodBase> written = [];

    private readonly Func<MethodBase, bool>? owned = ReversePatchFactory.OwnsBody;

    public static bool Unavailable => ForkRuntime.Unavailable || !ForkRuntime.HostRead;

    public void Dispose() {
        ReversePatchFactory.OwnsBody = owned;
        foreach (MethodBase target in written) {
            ForkRuntime.Restore(target);
        }
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void ACloneOfAnUntouchedBodyRunsThatBody() {
        MethodInfo target = typeof(Reversed).GetMethod(nameof(Reversed.Untouched))!;

        Assert.Equal(1, Invoke(OriginalBody.Clone(target)));
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void ACloneTakenAfterAWriteStillRunsTheOriginalBody() {
        MethodInfo target = typeof(Reversed).GetMethod(nameof(Reversed.Cloned))!;
        int beforeWrite = Invoke(OriginalBody.Clone(target));

        Write(target, 5);
        Assert.Equal(5, Reversed.Cloned());

        int afterWrite = Invoke(OriginalBody.Clone(target));

        Assert.Equal(1, beforeWrite);
        Assert.True(
            afterWrite == 1,
            $"OriginalBody.Clone returned a body that runs as {afterWrite}, and the unmodified method runs as 1. " +
            "The clone is reading the replaced body rather than the original, so every caller that asks Concord " +
            "for unpatched behaviour gets the patched behaviour instead. BodyReader.Open hands the method to " +
            "MonoMod's DynamicMethodDefinition, which reads it through MethodBase.GetMethodBody(), and on this " +
            "runtime that read returns whatever body is live. PublishedBodyTests.AReadReturnsTheOriginalBodyNot" +
            "TheLiveOne measures the same change one layer down.");
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void AReversePatchTakenAfterAWriteStillRunsTheOriginalBody() {
        MethodInfo target = typeof(Reversed).GetMethod(nameof(Reversed.Bound))!;
        ReversePatchFactory.OwnsBody = null;

        Write(target, 5);
        Assert.Equal(5, Reversed.Bound());

        int reversed = ((Func<int>)ReversePatchFactory.Bind(target, typeof(Func<int>)))();

        Assert.True(
            reversed == 1,
            $"ReversePatchFactory.Bind returned a delegate that runs as {reversed}, and the unmodified method " +
            "runs as 1. With OwnsBody null, Bind takes the OriginalBody.Clone branch, which its own summary " +
            "calls 'a clone of the original that bypasses every patch'. A mod reaching for vanilla behaviour " +
            "through a reverse patch gets the patched behaviour back with nothing reporting it.");
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void ACloneOfAGuardedBodyKeepsItsLocalsAndItsHandlersAfterAWrite() {
        MethodInfo target = typeof(Reversed).GetMethod(nameof(Reversed.Guarded))!;
        Assert.Equal(11, Reversed.Guarded(5));

        Write(target, 5);
        Assert.Equal(5, Reversed.Guarded(5));

        MethodInfo clone = OriginalBody.Clone(target);

        Assert.Equal(11, (int)clone.Invoke(null, [5])!);
        Assert.Equal(0, (int)clone.Invoke(null, [int.MaxValue])!);
    }

    private int Invoke(MethodInfo body) {
        return (int)body.Invoke(null, null)!;
    }

    private void Write(MethodBase target, int value) {
        ForkRuntime.Write(target, value);
        written.Add(target);
    }
}
