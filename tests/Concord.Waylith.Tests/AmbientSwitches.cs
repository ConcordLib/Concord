using System.Runtime.CompilerServices;
using Concord.Backend;

namespace Concord.Waylith.Tests;

internal static class AmbientSwitches {
    internal static IPatchBackend? InstalledBackend() {
        try {
            return PatchBackend.Current;
        } catch (InvalidOperationException) {
            return null;
        }
    }

    internal static void RestoreBackend(IPatchBackend? backend) {
        if (backend is not null) {
            PatchBackend.Current = backend;
        }
    }

    [ModuleInitializer]
    internal static void Neutralize() {
        Environment.SetEnvironmentVariable(AdapterEntryPoint.EmptyMapVariable, null);
    }
}
