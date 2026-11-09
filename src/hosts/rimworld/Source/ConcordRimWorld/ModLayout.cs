using System.Reflection;

namespace Concord.RimWorld;

internal static class ModLayout {
  private const string GameVersionKey = "GameVersion";

  internal static string VersionDir => field ??= ReadGameVersion(typeof(ModLayout).Assembly);

  internal static string SimpleName(Assembly assembly) {
    string full = assembly.FullName;
    int comma = full.IndexOf(',');
    return comma < 0 ? full : full.Substring(0, comma);
  }

  internal static string BuildSha(Assembly assembly) {
    AssemblyInformationalVersionAttribute stamp = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();

    if (stamp?.InformationalVersion == null) {
      return null;
    }

    int plus = stamp.InformationalVersion.IndexOf('+');
    return plus < 0 ? null : stamp.InformationalVersion.Substring(plus + 1);
  }

  private static string ReadGameVersion(Assembly assembly) {
    foreach (AssemblyMetadataAttribute stamp in assembly.GetCustomAttributes<AssemblyMetadataAttribute>()) {
      if (stamp.Key == GameVersionKey) {
        return stamp.Value;
      }
    }

    return "1.7";
  }
}
