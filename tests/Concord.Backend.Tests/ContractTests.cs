using System.Reflection;
using Concord.Emit;
using Concord.Tests.Shared;
using Xunit;

namespace Concord.Backend.Tests;

public class ContractTests {
    private static readonly MethodBase Src = typeof(Targets).GetMethod(nameof(Targets.Original))!;

    [Fact]
    public void ApplyComposed_returns_an_applied_handle_for_the_original() {
        IPatchBackend backend = new RecordingDetourBackend();
        IBackendHandle handle = backend.ApplyComposed(Src, []);
        Assert.Same(Src, handle.Original);
        Assert.True(handle.IsApplied);
    }

    [Fact]
    public void Disposing_the_handle_marks_it_not_applied() {
        IPatchBackend backend = new RecordingDetourBackend();
        IBackendHandle handle = backend.ApplyComposed(Src, []);
        handle.Dispose();
        Assert.False(handle.IsApplied);
    }

    [Fact]
    public void Double_dispose_is_safe() {
        IBackendHandle handle = new RecordingDetourBackend().ApplyComposed(Src, []);
        handle.Dispose();
        handle.Dispose();
        Assert.False(handle.IsApplied);
    }
}
