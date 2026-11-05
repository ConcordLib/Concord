using System.Reflection;
using Concord.Detour;
using Concord.Orchestration.Tests.RegistryAssembly;
using Xunit;

namespace Concord.Orchestration.Tests;

public sealed class PatchRegistryTests : IDisposable {
    private readonly RecordingDetourBackend backend = new RecordingDetourBackend();

    public PatchRegistryTests() {
        DetourBackend.Current = backend;
    }

    public void Dispose() {
        DetourBackend.Reset();
    }

    [Fact]
    public void TryGetRegistryDeclarations_AssemblyWithRegistry_ReturnsListedDeclarations() {
        bool found = PatchDeclarationScanner.TryGetRegistryDeclarations(
            typeof(RegistryTarget).Assembly, out IReadOnlyList<Type> declarations);

        Assert.True(found);
        Assert.Equal([typeof(RegistryListedPatch)], declarations);
    }

    [Fact]
    public void TryGetRegistryDeclarations_AssemblyWithoutRegistry_ReturnsFalse() {
        bool found = PatchDeclarationScanner.TryGetRegistryDeclarations(
            typeof(PatchRegistryTests).Assembly, out IReadOnlyList<Type> declarations);

        Assert.False(found);
        Assert.Empty(declarations);
    }

    [Fact]
    public void Apply_AssemblyWithRegistry_AppliesListedDeclarationsOnly() {
        MethodBase bump = typeof(RegistryTarget).GetMethod(nameof(RegistryTarget.Bump))!;

        using (Patcher.Apply(typeof(RegistryTarget).Assembly)) {
            AppliedInjection applied = Assert.Single(backend.Applied);

            Assert.Equal(bump, applied.Target);
            Assert.Equal(typeof(RegistryListedPatch), applied.Injection.InjectionMethod.DeclaringType);
        }
    }
}
