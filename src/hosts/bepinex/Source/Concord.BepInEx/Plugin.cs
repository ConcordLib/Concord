using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using Concord.HarmonyX;

namespace Concord.BepInEx;

[BepInPlugin(Guid, "Concord", BuildVersion.Value)]
public sealed class Plugin : BaseUnityPlugin {
    public const string Guid = "concordlib.concord";

    private static ManualLogSource log;

    private void Awake() {
        log = Logger;
        log.LogInfo(VersionInfo.Line + " loading.");

        if (Type.GetType("Mono.Runtime") == null) {
            log.LogError("Concord.BepInEx only supports Unity Mono runtimes. Nothing was wired.");
            return;
        }

        string pluginDir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);

        HostSurface.Report = message => log.LogError(message);

        try {
            Adapter.Wire(new WireContext {
                InstallBackend = () => InstallBackend(Paths.BepInExAssemblyDirectory),
                Log = message => log.LogWarning(message),
                LogError = message => HostSurface.Report(message)
            });

            RuntimeGuard.Install(pluginDir, message => log.LogError(message));
            log.LogInfo("Concord runtime wired.");
        } catch (Exception e) {
            log.LogError("Failed to initialize Concord: " + e);
        }
    }

    private static bool InstallBackend(string bepInExCoreDir) {
        Action<string> logError = message => log.LogError(message);
        Assembly harmony = HarmonyProbe.FindCoreHarmony(
            () => AppDomain.CurrentDomain.GetAssemblies(), bepInExCoreDir, logError);

        if (harmony == null) {
            logError("HarmonyX is not loaded from BepInEx/core, so Concord has no backend to patch through this session.");
            return false;
        }

        HarmonyXInstallResult result = HarmonyXEntryPoint.Install(message => log.LogWarning(message), null);
        log.LogInfo(result.Message);
        return result.Installed;
    }
}
