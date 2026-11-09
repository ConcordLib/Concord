using Concord.Tests.Shared;
using Xunit;

namespace Concord.Backend.Tests;

public class DetourBackendTests {
    [Fact]
    public void Current_can_be_swapped_and_reset() {
        try {
            RecordingDetourBackend fake = new RecordingDetourBackend();
            PatchBackend.Current = fake;
            Assert.Same(fake, PatchBackend.Current);
        } finally {
            PatchBackend.Reset();
        }
    }

    [Fact]
    public void Current_throws_before_anything_set_it() {
        PatchBackend.Reset();

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => PatchBackend.Current);
        Assert.Contains("No patch backend is installed", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("loader installs one", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("refused this runtime", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("switched off", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("'backend:' line", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("never ran", thrown.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Waylith", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Current_rejects_null() {
        Assert.Throws<ArgumentNullException>(() => PatchBackend.Current = null!);
    }
}
