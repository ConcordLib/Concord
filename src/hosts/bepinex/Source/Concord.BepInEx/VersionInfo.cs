using System;
using System.Reflection;

namespace Concord.BepInEx;

internal static class VersionInfo {
    private static string runtime;

    internal static string Adapter => BuildVersion.Value;

    internal static string Runtime => runtime ??= Read(typeof(Patcher).Assembly);

    internal static string Line => "Concord v" + Adapter + " (runtime v" + Runtime + ")";

    private static string Read(Assembly assembly) {
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(version)) {
            return assembly.GetName().Version?.ToString() ?? "unknown";
        }

        int metadata = version.IndexOf('+');
        return metadata < 0 ? version : version.Substring(0, metadata);
    }
}
