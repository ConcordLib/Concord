using System.Reflection;
using Concord.Backend;
using Concord.Tests.Shared;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Concord.Generators.Tests.Consumer;

public sealed class EndToEndTests {
    [Fact]
    public void Apply_ComposesEveryDeclaredTarget() {
        RecordingDetourBackend recorder = new RecordingDetourBackend();
        PatchBackend.Current = recorder;
        List<string> logs = [];
        Patcher.UseLog(logs.Add);

        MethodBase tick = typeof(SecretCounter).GetMethod(nameof(SecretCounter.Tick))!;
        MethodBase ring = typeof(SurfaceRegister).GetMethod(nameof(SurfaceRegister.Ring))!;

        try {
            using (Patcher.Apply(typeof(SecretCounterPatch).Assembly)) {
                List<MethodBase> targets = recorder.ComposedDetours.Select(entry => entry.Target).ToList();

                Assert.Contains(tick, targets);
                Assert.Contains(ring, targets);
                Assert.Equal([typeof(SecretCounterPatch).FullName!], recorder.OwnersOf(tick));
                Assert.Equal(
                    [
                        typeof(SurfaceCapturePatch).FullName!,
                        typeof(SurfaceNewPatch).FullName!,
                        typeof(SurfaceSlicePatch).FullName!,
                        typeof(SurfaceStatePatch).FullName!,
                    ],
                    recorder.OwnersOf(ring).OrderBy(owner => owner, StringComparer.Ordinal));
            }

            Assert.Empty(logs);
        } finally {
            Patcher.UseLog(null);
        }
    }
}
