using System;
using System.IO;
using System.Reflection;

namespace Concord.BepInEx;

public static class RuntimeGuard {
    private static AssemblyLoadEventHandler handler;

    public static void Install(string pluginDir, Action<string> logError) {
        if (handler != null) {
            return;
        }

        handler = (sender, args) => {
            string message = Describe(args.LoadedAssembly, pluginDir);
            if (message != null) {
                logError(message);
            }
        };

        AppDomain.CurrentDomain.AssemblyLoad += handler;

        foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies()) {
            string message = Describe(loaded, pluginDir);
            if (message != null) {
                logError(message);
            }
        }
    }

    internal static string Describe(Assembly loaded, string pluginDir) {
        if (loaded.GetName().Name != "Concord") {
            return null;
        }

        string location;
        try {
            location = loaded.Location;
        } catch (NotSupportedException) {
            location = null;
        }

        if (string.IsNullOrEmpty(location)) {
            return "A second Concord runtime was loaded from memory. Patches applied through it are invisible to the Concord router. Remove the bundled Concord.dll from that mod.";
        }

        string full = Path.GetFullPath(pluginDir);
        if (full[full.Length - 1] != Path.DirectorySeparatorChar) {
            full += Path.DirectorySeparatorChar;
        }

        if (Path.GetFullPath(location).StartsWith(full, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        return "A second Concord runtime was loaded from " + location + ". Patches applied through it are invisible to the Concord router. Remove the bundled Concord.dll from that mod.";
    }

    internal static void ResetForTests() {
        if (handler != null) {
            AppDomain.CurrentDomain.AssemblyLoad -= handler;
            handler = null;
        }
    }
}
