using System;
using System.IO;
using System.Reflection;

namespace Concord.BepInEx;

public static class HarmonyProbe {
    internal static Assembly FindCoreHarmony(Func<Assembly[]> loadedAssemblies, string bepInExCoreDir, Action<string> log) {
        Assembly harmony = Array.Find(loadedAssemblies(), a => a.GetName().Name == "0Harmony");
        if (harmony == null) {
            return null;
        }

        string location = Location(harmony);
        if (location == null) {
            log("0Harmony is loaded from memory, so it cannot be traced to BepInEx/core. Concord will not patch anything this session.");
            return null;
        }

        if (!IsUnder(location, bepInExCoreDir)) {
            log("0Harmony at " + location + " is outside BepInEx/core. Concord will not patch anything this session.");
            return null;
        }

        return harmony;
    }

    private static string Location(Assembly assembly) {
        try {
            string location = assembly.Location;
            return string.IsNullOrEmpty(location) ? null : Path.GetFullPath(location);
        } catch (NotSupportedException) {
            return null;
        }
    }

    private static bool IsUnder(string path, string root) {
        string full;
        try {
            full = Path.GetFullPath(root);
        } catch (Exception) {
            return false;
        }

        if (full[full.Length - 1] != Path.DirectorySeparatorChar) {
            full += Path.DirectorySeparatorChar;
        }

        return path.StartsWith(full, StringComparison.OrdinalIgnoreCase);
    }
}
