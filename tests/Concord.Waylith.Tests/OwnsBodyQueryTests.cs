using System.Reflection;
using Concord.Backend;
using Xunit;

namespace Concord.Waylith.Tests;

public sealed class OwnsBodyQueryTests {
    public static int Subject() {
        return 1;
    }

    [Fact]
    public void TheBackendAnswersTheOwnsBodyQuery() {
        Assert.IsAssignableFrom<IPatchBackend>(Backend(new FakeBodyOwner()));
    }

    [Fact]
    public void AnUnpublishedTargetIsNotOwned() {
        IPatchBackend backend = Backend(new FakeBodyOwner());

        Assert.False(backend.OwnsBody(Target()));
    }

    [Fact]
    public void APublishedTargetIsOwned() {
        IPatchBackend backend = Backend(new FakeBodyOwner { Published = true });

        Assert.True(backend.OwnsBody(Target()));
    }

    [Fact]
    public void RecomposeForwardsThePublishersRebuildAnswer() {
        IPatchBackend backend = Backend(new FakeBodyOwner { Rebuilt = true });

        Assert.True(backend.Recompose(Target()));
    }

    [Fact]
    public void RecomposeForwardsAFalseAnswer() {
        IPatchBackend backend = Backend(new FakeBodyOwner { Rebuilt = false });

        Assert.False(backend.Recompose(Target()));
    }

    private static MethodBase Target() {
        return typeof(OwnsBodyQueryTests).GetMethod(nameof(Subject))!;
    }

    private static WaylithClrBackend Backend(ITargetBodyOwner owner) {
        return new WaylithClrBackend(owner);
    }

    private sealed class FakeBodyOwner : ITargetBodyOwner {
        public bool Published { get; set; }

        public bool Rebuilt { get; set; }

        public int PublishedTargetCount => 0;

        public bool PublishComposed(MethodBase target, IReadOnlyList<Injection> ordered) {
            return true;
        }

        public void RevertToOriginal(MethodBase target) {
        }

        public bool IsPublished(MethodBase target) {
            return Published;
        }

        public bool Rebuild(MethodBase target) {
            return Rebuilt;
        }
    }
}
