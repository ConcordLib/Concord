using System.Reflection;
using Concord.Detour;

namespace Concord.Host.Adapter;

internal static class StaleBodyHandler {
    internal const string RefusedMarker = "[Concord.Waylith] stale-refused";

    internal const string NoRecomposeMarker = "[Concord.Waylith] stale-no-recompose";

    internal const string FaultMarker = "[Concord.Waylith] stale-failed";

    private const string RegisterEntry = "RegisterStaleMethodHandler";

    private const string RewrittenEntry = "HasRewrittenBody";

    private const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.Static;

    private static readonly Func<MethodBase, bool> HostRewroteBody = BindRewrittenBody();

    private static Action<string>? logger;

    private static bool registered;

    internal static Func<MethodBase, bool>? RecomposeOverride { get; set; }

    internal static Func<MethodBase, bool>? RewrittenBodyOverride { get; set; }

    internal static bool Handle(MethodBase stale) {
        string? named = null;
        try {
            named = WaylithDetourBackend.Describe(stale);

            MethodBase? target = MethodIdentity.ResolveOriginal(stale);
            if (target is null) {
                return false;
            }

            named = WaylithDetourBackend.Describe(target);

            if ((RewrittenBodyOverride ?? HostRewroteBody)(target)) {
                logger?.Invoke(
                    RefusedMarker + " '" + named + "': the host rewrote this body " +
                    "itself. Two writers on one body do not compose and publishing replaces rather than chains, so " +
                    "recomposing here would throw away whichever of them wrote last. Concord refuses it and hands " +
                    "the method to the next handler.");
                return false;
            }

            return (RecomposeOverride ?? RecomposeThroughBackend)(target);
        } catch (Exception failure) {
            logger?.Invoke(
                FaultMarker + " '" + (named ?? stale.Name) + "': " + failure.GetType().Name + ", " +
                failure.Message.TrimEnd('.') + ". Every patch on that method stays poisoned until something " +
                "patches it again. The rest of the handler chain still runs, because a throw out of here would " +
                "end it and take harmony's handler down with Concord's.");
            return false;
        }
    }

    internal static void Install(Action<string> log) {
        logger = log;

        if (registered || InjectionCapability.Unavailable) {
            return;
        }

        try {
            Type handlerDelegate = InjectionCapability.Delegate()!;
            MethodInfo register = InjectionCapability.Injection()!
                .GetMethod(RegisterEntry, AnyStatic, [handlerDelegate])!;
            Func<MethodBase, bool> body = Handle;

            register.Invoke(null, [Delegate.CreateDelegate(handlerDelegate, body.Target, body.Method)]);
            registered = true;
        } catch (Exception thrown) {
            Exception failure = (thrown as TargetInvocationException)?.InnerException ?? thrown;
            log(
                FaultMarker + " registering with '" + InjectionCapability.InjectionTypeName + "'." + RegisterEntry +
                " threw " + failure.GetType().Name + ", " + failure.Message.TrimEnd('.') + ". Nothing will tell " +
                "Concord when a mod injecting a field poisons its wrappers, so the first sign will be a patched " +
                "method throwing.");
        }
    }

    private static bool RecomposeThroughBackend(MethodBase target) {
        IDetourBackend current = DetourBackend.Current;
        IDetourBackend inner = current is RoutingDetourBackend router ? router.Inner : current;

        if (inner is not IRecomposeTarget recomposer) {
            logger?.Invoke(
                NoRecomposeMarker + " '" + WaylithDetourBackend.Describe(target) + "': the installed backend '" +
                inner.GetType().FullName + "' does not implement '" + typeof(IRecomposeTarget).FullName + "', so " +
                "nothing can rebuild the wrapper this relayout poisoned and every patch on that method is dead " +
                "for the rest of the session.");
            return false;
        }

        return recomposer.Recompose(target);
    }

    private static Func<MethodBase, bool> BindRewrittenBody() {
        if (InjectionCapability.Unavailable) {
            return static _ => false;
        }

        return InjectionCapability.Injection()!
            .GetMethod(RewrittenEntry, AnyStatic, [typeof(MethodBase)])!
            .CreateDelegate<Func<MethodBase, bool>>();
    }
}
