using System.Reflection;

namespace Concord.Waylith.ForkTests;

public static class ForkInjection {
    public const string InjectionTypeName = "Waylith.Injection.Injection, Waylith.Injection";

    public const string DelegateTypeName = "Waylith.Injection.Injection+HarmonyRegenerateDelegate, Waylith.Injection";

    private const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.Static;

    private static readonly Type? Injection = Type.GetType(InjectionTypeName, throwOnError: false);

    private static readonly Type? DelegateType = Type.GetType(DelegateTypeName, throwOnError: false);

    private static readonly PropertyInfo? Supported = Injection?.GetProperty("IsSupported", AnyStatic);

    private static readonly MethodInfo? Register = DelegateType is null
        ? null
        : Injection?.GetMethod("RegisterStaleMethodHandler", AnyStatic, [DelegateType]);

    private static readonly Type? ContinuationType = ForkFieldInjection.BatchType is null
        ? null
        : typeof(Action<>).MakeGenericType(ForkFieldInjection.BatchType);

    private static readonly MethodInfo? QueueEntry = ContinuationType is null
        ? null
        : Injection?.GetMethod(
            "QueueForFrameBoundary",
            AnyStatic,
            [ForkFieldInjection.BatchType!, ContinuationType]);

    private static readonly MethodInfo? ApplyAtBoundary = Injection?.GetMethod(
        "ApplyPendingAtFrameBoundary",
        AnyStatic,
        Type.EmptyTypes);

    public static bool IsSupported => Supported?.GetValue(null) is true;

    public static bool Unavailable =>
        Injection is null || !IsSupported || Register is null || QueueEntry is null || ApplyAtBoundary is null;

    public static void RegisterStaleMethodHandler(Func<MethodBase, bool> handler) {
        ForkFieldInjection.Unwrapped(() => {
            Register!.Invoke(null, [Delegate.CreateDelegate(DelegateType!, handler.Target, handler.Method)]);
            return null;
        });
    }

    public static void QueueForFrameBoundary(ForkFieldInjection.Batch batch, Action onApplied) {
        Action<object> relay = _ => onApplied();
        Delegate continuation = Delegate.CreateDelegate(ContinuationType!, relay.Target, relay.Method);

        ForkFieldInjection.Unwrapped(() => {
            QueueEntry!.Invoke(null, [batch.Inner, continuation]);
            return null;
        });
    }

    public static void ApplyPendingAtFrameBoundary() {
        ForkFieldInjection.Unwrapped(() => {
            ApplyAtBoundary!.Invoke(null, null);
            return null;
        });
    }
}
