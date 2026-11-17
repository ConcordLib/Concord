using System.Reflection;

namespace Concord.Waylith;

internal static class HostRegenerate {
    internal const string FailedMarker = "[Concord.Waylith] regenerate-hook-failed";

    private const string Owner = "Waylith.Injection.Injection, Waylith.Injection";

    // TODO(harmony): delete this class when the harmony mod stage rebuilds on a changed input
    private const string Slot = "HarmonyRegenerate";

    private static readonly PropertyInfo? Hook =
        Type.GetType(Owner, throwOnError: false)?.GetProperty(Slot, BindingFlags.Public | BindingFlags.Static);

    internal static void Run(MethodBase target, Action<string> log) {
        if (Hook?.GetValue(null) is not Delegate regenerate) {
            return;
        }

        try {
            regenerate.DynamicInvoke(target);
        } catch (TargetInvocationException thrown) when (thrown.InnerException is not OutOfMemoryException) {
            Exception failure = thrown.InnerException ?? thrown;
            log(
                FailedMarker + " '" + WaylithClrBackend.Describe(target) + "': " + failure.GetType().Name + ", " +
                failure.Message.TrimEnd('.') + ". The host's regenerate hook threw after Concord rebuilt that " +
                "method, so a later stage that was built on the old body may no longer run on it.");
        }
    }
}
