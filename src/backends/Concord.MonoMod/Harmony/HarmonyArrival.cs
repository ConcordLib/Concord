using System;
using System.Reflection;

namespace Concord.MonoMod.Harmony;

/// <summary>Tells a loaded Harmony apart from any other assembly by its simple name.</summary>
public static class HarmonyArrival
{
    internal const string ArmFailedMarker = "[Concord.Coex] arrival-failed";

    private const string HarmonyName = "0Harmony";

    internal static bool IsHarmony(Assembly assembly)
    {
        string? full = assembly.FullName;
        if (full == null)
        {
            return false;
        }

        int comma = full.IndexOf(',');
        string simple = comma < 0 ? full : full.Substring(0, comma);
        return string.Equals(simple, HarmonyName, StringComparison.Ordinal);
    }
}
