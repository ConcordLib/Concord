using System.Reflection;
using Concord.Backend;
using Concord.Tests.Shared;
using Xunit;

namespace Concord.Generators.Tests.Consumer;

public sealed class EndToEndTests {
    [Fact]
    public void GeneratorEmittedRegistry_IsOnTheAssembly() {
        PatchRegistryAttribute? attribute =
            typeof(SecretCounterPatch).Assembly.GetCustomAttribute<PatchRegistryAttribute>();

        Assert.NotNull(attribute);
        Assert.True(typeof(IPatchDeclarationProvider).IsAssignableFrom(attribute!.RegistryType));
    }

    [Fact]
    public void GeneratedRegistry_ComposesEveryDeclaredTarget() {
        RecordingDetourBackend recorder = new RecordingDetourBackend();
        PatchBackend.Current = recorder;

        MethodBase tick = typeof(SecretCounter).GetMethod(nameof(SecretCounter.Tick))!;
        MethodBase ring = typeof(SurfaceRegister).GetMethod(nameof(SurfaceRegister.Ring))!;

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
    }
}
