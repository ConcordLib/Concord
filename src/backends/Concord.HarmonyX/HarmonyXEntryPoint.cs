using Concord.Backend;
using Concord.MonoMod;

namespace Concord.HarmonyX;

/// <summary>The install hook a HarmonyX host calls to give Concord a patch backend.</summary>
public static class HarmonyXEntryPoint {
    private static readonly object Gate = new object();

    private static HarmonyXBackend? installed;

    /// <summary>Installs the HarmonyX backend and makes it the one Concord patches through.</summary>
    /// <param name="log">Where the backend writes its log lines, or <see langword="null" /> to keep the current sink.</param>
    /// <param name="assemblyDirectories">Directories the host loads its own assemblies from, which this backend does not need because it binds the host's loose copies.</param>
    /// <returns>Whether the backend installed, and the one line to log for it.</returns>
    public static HarmonyXInstallResult Install(Action<string>? log, string[]? assemblyDirectories) {
        _ = assemblyDirectories;
        if (log is not null) {
            HarmonyXBackend.Log = log;
        }

        lock (Gate) {
            MonoModEmit.InstallScratchGenerator();
            installed ??= new HarmonyXBackend();
            PatchBackend.Current = installed;
        }

        return new HarmonyXInstallResult(
            true,
            "backend: " + HarmonyXBackend.BackendName + " installed, one ILHook per target inside HarmonyX's chain.");
    }
}
