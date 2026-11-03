using System.Runtime.CompilerServices;

namespace Concord.Host.Adapter.Tests;

internal static class AmbientSwitches {
    internal static readonly Func<Action<string>, InjectionCapabilityGap> SilentStaleHandler =
        static _ => InjectionCapabilityGap.None;

    [ModuleInitializer]
    internal static void Neutralize() {
        Environment.SetEnvironmentVariable(AdapterEntryPoint.EmptyMapVariable, null);
        AdapterEntryPoint.StaleHandler = SilentStaleHandler;
    }
}
