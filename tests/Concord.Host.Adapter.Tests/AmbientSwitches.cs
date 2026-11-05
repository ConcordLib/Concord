using System.Runtime.CompilerServices;
using Concord.Detour;

namespace Concord.Host.Adapter.Tests;

internal static class AmbientSwitches {
    internal static IDetourBackend? InstalledBackend() {
        try {
            return DetourBackend.Current;
        } catch (InvalidOperationException) {
            return null;
        }
    }

    internal static void RestoreBackend(IDetourBackend? backend) {
        if (backend is not null) {
            DetourBackend.Current = backend;
        }
    }

    [ModuleInitializer]
    internal static void Neutralize() {
        Environment.SetEnvironmentVariable(AdapterEntryPoint.EmptyMapVariable, null);
    }
}
