using System.Reflection;
using Concord.Detour;
using Xunit;

namespace Concord.Host.Adapter.Tests;

public static class StagePublisherSubjects {
    public static int First(int value) {
        return value;
    }

    public static int Second(int value) {
        return value + 1;
    }
}

public sealed class StagePublisherTests {
    private static readonly MethodBase Target = typeof(StagePublisherSubjects).GetMethod(nameof(StagePublisherSubjects.First))!;

    [Fact]
    public void PublishingAddsAnOwnerToTheTargetsRecord() {
        StagePublisher publisher = Fresh(out RuntimeMethodHandle handle);

        publisher.PublishComposed(Target, [Owned("mod.a")]);

        Assert.Equal(["mod.a"], publisher.Staged(handle)!.Ordered.Select(injection => injection.Owner));
    }

    [Fact]
    public void RevertingRemovesTheRecordEntirely() {
        StagePublisher publisher = Fresh(out RuntimeMethodHandle handle);
        publisher.PublishComposed(Target, [Owned("mod.a")]);

        publisher.RevertToOriginal(Target);

        Assert.Null(publisher.Staged(handle));
        Assert.False(publisher.IsPublished(Target));
    }

    [Fact]
    public void AnInFlightReadKeepsItsSnapshotWhileAWriterReplacesTheRecord() {
        StagePublisher publisher = Fresh(out RuntimeMethodHandle handle);
        publisher.PublishComposed(Target, [Owned("mod.a")]);
        StagedTarget held = publisher.Staged(handle)!;

        publisher.PublishComposed(Target, [Owned("mod.a"), Owned("mod.b")]);

        Assert.Equal(["mod.a"], held.Ordered.Select(injection => injection.Owner));
        Assert.Equal(["mod.a", "mod.b"], publisher.Staged(handle)!.Ordered.Select(injection => injection.Owner));
    }

    [Fact]
    public void AFailedRebuildMarksTheTargetSoItNoLongerReportsPublished() {
        StagePublisher publisher = Fresh(out _);
        publisher.PublishComposed(Target, [Owned("mod.a")]);
        publisher.UseRebuild(static _ => new InvalidOperationException("the runtime refused this handle"));

        publisher.PublishComposed(Target, [Owned("mod.a"), Owned("mod.b")]);

        Assert.False(publisher.IsPublished(Target));
    }

    [Fact]
    public void TheNextSuccessfulPublishClearsAPriorFailure() {
        StagePublisher publisher = Fresh(out _);
        publisher.PublishComposed(Target, [Owned("mod.a")]);
        publisher.UseRebuild(static _ => new InvalidOperationException("the runtime refused this handle"));
        publisher.PublishComposed(Target, [Owned("mod.a"), Owned("mod.b")]);

        publisher.UseRebuild(static _ => null);
        publisher.PublishComposed(Target, [Owned("mod.a"), Owned("mod.b")]);

        Assert.True(publisher.IsPublished(Target));
    }

    [Fact]
    public void TheNextSuccessfulRebuildClearsAPriorFailure() {
        StagePublisher publisher = Fresh(out RuntimeMethodHandle handle);
        publisher.PublishComposed(Target, [Owned("mod.a")]);
        publisher.Failed(handle, new InvalidOperationException("the runtime refused this handle"));
        Assert.False(publisher.IsPublished(Target));

        publisher.UseRebuild(static _ => null);
        Assert.True(publisher.Rebuild(Target));

        Assert.True(publisher.IsPublished(Target));
    }

    [Fact]
    public void TwoDifferentFailuresOnOneTargetEachLogOnce() {
        List<string> lines = [];
        StagePublisher publisher = Fresh(out _, lines.Add);
        publisher.PublishComposed(Target, [Owned("mod.a")]);

        publisher.UseRebuild(static _ => new InvalidOperationException("the runtime refused this handle"));
        publisher.PublishComposed(Target, [Owned("mod.a"), Owned("mod.b")]);

        publisher.UseRebuild(static _ => new InvalidOperationException("the body no longer matches the IL"));
        publisher.PublishComposed(Target, [Owned("mod.a"), Owned("mod.b")]);

        Assert.Equal(2, lines.Count);
    }

    [Fact]
    public void TheSameFailureTwiceLogsOnce() {
        List<string> lines = [];
        StagePublisher publisher = Fresh(out _, lines.Add);
        publisher.PublishComposed(Target, [Owned("mod.a")]);
        publisher.UseRebuild(static _ => new InvalidOperationException("the runtime refused this handle"));

        publisher.PublishComposed(Target, [Owned("mod.a"), Owned("mod.b")]);
        publisher.PublishComposed(Target, [Owned("mod.a"), Owned("mod.b")]);

        Assert.Single(lines);
    }

    private static StagePublisher Fresh(out RuntimeMethodHandle handle) {
        return Fresh(out handle, static _ => { });
    }

    private static StagePublisher Fresh(out RuntimeMethodHandle handle, Action<string> log) {
        handle = Target.MethodHandle;
        StagePublisher publisher = new StagePublisher(method => method.MethodHandle, log);
        publisher.UseRebuild(static _ => null);
        return publisher;
    }

    private static Injection Owned(string owner) {
        return new Injection(Target, new InjectAt.Head(), owner, 0);
    }
}
