using Concord.Tests.Shared;
using Xunit;

namespace Concord.Detour.Tests;

public class DetourBackendTests {
    [Fact]
    public void Current_can_be_swapped_and_reset() {
        try {
            RecordingDetourBackend fake = new RecordingDetourBackend();
            DetourBackend.Current = fake;
            Assert.Same(fake, DetourBackend.Current);
        } finally {
            DetourBackend.Reset();
        }
    }

    [Fact]
    public void Current_throws_before_anything_set_it() {
        DetourBackend.Reset();

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => DetourBackend.Current);
        Assert.Contains("WaylithCLR 10.0.15", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("AdapterEntryPoint.Install", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("refused", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("switched off", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("[Concord.Waylith] backend:", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Current_rejects_null() {
        Assert.Throws<ArgumentNullException>(() => DetourBackend.Current = null!);
    }
}
