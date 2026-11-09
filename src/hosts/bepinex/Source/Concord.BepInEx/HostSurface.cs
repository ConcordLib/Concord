using System;

namespace Concord.BepInEx;

public static class HostSurface {
    public static Action<string> Report = _ => { };

    internal static void ResetForTests() {
        Report = _ => { };
    }
}
