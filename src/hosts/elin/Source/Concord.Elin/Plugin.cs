using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Logging;

namespace Concord.Elin;

[BepInPlugin("concordlib.concord.elin", "Concord for Elin", BuildVersion.Value)]
public sealed class Plugin : BaseUnityPlugin {
    private static ManualLogSource log;

    private void Awake() {
        log = Logger;

        bool hostPresent = false;
        Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < loaded.Length; i++) {
            if (loaded[i].GetName().Name == "Concord.BepInEx") {
                hostPresent = true;
                break;
            }
        }

        if (!hostPresent) {
            log.LogError("Concord for Elin needs the Concord BepInEx plugin. Install it at BepInEx/plugins/Concord (github.com/ConcordLib/BepInEx) and restart the game.");
            return;
        }

        try {
            Wire();
            log.LogInfo("Concord for Elin wired.");
        } catch (Exception e) {
            log.LogError("Failed to wire Concord for Elin: " + e);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Wire() {
        ElinAttachedPropertyRegistry registry = new ElinAttachedPropertyRegistry(
            message => log.LogInfo(message),
            message => log.LogWarning(message),
            message => log.LogError(message));

        ElinRuntime.Registry = registry;
        SaveHooks.Log = message => log.LogError(message);

        Patcher.UseAttachedPropertyRegistry(registry);
        Patcher.Apply(typeof(Plugin).Assembly);
    }
}
