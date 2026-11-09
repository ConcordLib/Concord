using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Backend;
using Concord.Emit;
using Xunit;

namespace Concord.Orchestration.Tests;

public static class OwnersFrameInjections {
    public static void Capture(ControlHandle ch) {
    }
}

[Collection(SharedAssemblyApplyCollection.Name)]
public class PatchOwnersFrameTests : IDisposable {
    private readonly RecordingDetourBackend backend = new RecordingDetourBackend();

    public PatchOwnersFrameTests() {
        PatchBackend.Current = backend;
    }

    public void Dispose() {
        PatchBackend.Reset();
    }

    [Fact]
    public void OwnersOfFrame_NamesTheOwnersLiveOnTheFramesMethod() {
        StackFrame frame = CaptureFrame();

        using IPatchHandle handle = Patcher.PatchInjection(frame.GetMethod()!, Capture("mod.frame"));

        Assert.Equal(["mod.frame"], Patcher.OwnersOfFrame(frame));
    }

    [Fact]
    public void OwnersOfFrame_IsEmptyForAMethodConcordNeverPatched() {
        Assert.Empty(Patcher.OwnersOfFrame(CaptureFrame()));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static StackFrame CaptureFrame() {
        return new StackTrace().GetFrame(0)!;
    }

    private static Injection Capture(string owner) {
        MethodInfo capture = typeof(OwnersFrameInjections).GetMethod(nameof(OwnersFrameInjections.Capture))!;
        return new Injection(capture, new InjectAt.Tail(), owner, 0);
    }
}
