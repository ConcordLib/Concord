namespace Concord.BepInEx;

internal static class Adapter {
    private static bool wired;

    internal static bool Wired => wired;

    internal static void Wire(WireContext context) {
        if (wired) {
            return;
        }

        Patcher.UseLog(context.Log);

        if (!context.InstallBackend()) {
            return;
        }

        wired = true;
    }

    internal static void ResetForTests() {
        wired = false;
    }
}
