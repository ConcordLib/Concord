using System.Reflection;
using System.Reflection.Emit;

namespace Concord.Host.Adapter.ForkTests;

public enum InjectionGap {
    None,
    Capability,
    Api,
}

public static class ForkInjection {
    public const string InjectionTypeName = "Waylith.Injection.Injection, Waylith.Injection";

    public const string DelegateTypeName = "Waylith.Injection.Injection+HarmonyRegenerateDelegate, Waylith.Injection";

    public const string InjectedFieldAttributeTypeName = "Waylith.Injection.InjectedFieldAttribute, Waylith.Injection";

    private const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.Static;

    private static readonly Type? Injection = Type.GetType(InjectionTypeName, throwOnError: false);

    private static readonly Type? DelegateType = Type.GetType(DelegateTypeName, throwOnError: false);

    private static readonly PropertyInfo? Supported = Injection?.GetProperty("IsSupported", AnyStatic);

    private static readonly MethodInfo? Register = DelegateType is null
        ? null
        : Injection?.GetMethod("RegisterStaleMethodHandler", AnyStatic, [DelegateType]);

    private static readonly MethodInfo? HasRewritten = Injection?.GetMethod(
        "HasRewrittenBody",
        AnyStatic,
        [typeof(MethodBase)]);

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

    private static readonly PropertyInfo? Regenerate = Injection?.GetProperty("HarmonyRegenerate", AnyStatic);

    private static readonly MethodInfo? ProcessAssembliesEntry = Injection?.GetMethod(
        "ProcessAssemblies",
        AnyStatic,
        [typeof(IReadOnlyList<Assembly>)]);

    private static readonly Type? InjectedFieldAttributeType =
        Type.GetType(InjectedFieldAttributeTypeName, throwOnError: false);

    private static readonly ConstructorInfo? InjectedFieldAttributeCtor =
        InjectedFieldAttributeType?.GetConstructor(Type.EmptyTypes);

    public static InjectionGap Gap =>
        InjectionCapability.Unavailable ? InjectionGap.Capability
        : QueueEntry is null || ApplyAtBoundary is null || Regenerate is null || HasRewritten is null
            || ProcessAssembliesEntry is null || InjectedFieldAttributeCtor is null
            ? InjectionGap.Api
        : InjectionGap.None;

    public static bool Unavailable => Gap != InjectionGap.None;

    public static bool IsSupported => Supported?.GetValue(null) is true;

    public static string Explain => Gap switch {
        InjectionGap.Api =>
            $"'{InjectionTypeName}' is present on '{RuntimeCapability.Runtime}' but does not expose " +
            "QueueForFrameBoundary, ApplyPendingAtFrameBoundary and HarmonyRegenerate, so this is not the api " +
            "these facts were written against",
        _ => InjectionCapability.Explain(),
    };

    public static Delegate NewHandler(Func<MethodBase, bool> body) {
        return Delegate.CreateDelegate(DelegateType!, body.Target, body.Method);
    }

    public static void RegisterStaleMethodHandler(Func<MethodBase, bool> handler) {
        ForkFieldInjection.Unwrapped(() => {
            Register!.Invoke(null, [NewHandler(handler)]);
            return null;
        });
    }

    public static bool HasRewrittenBody(MethodBase method) {
        return (bool)ForkFieldInjection.Unwrapped(() => HasRewritten!.Invoke(null, [method]))!;
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

    public static Delegate? HarmonyRegenerate {
        get => (Delegate?)Regenerate!.GetValue(null);
        set => Regenerate!.SetValue(null, value);
    }

    public static void ProcessAssemblies(IReadOnlyList<Assembly> assemblies) {
        ForkFieldInjection.Unwrapped(() => {
            ProcessAssembliesEntry!.Invoke(null, [assemblies]);
            return null;
        });
    }

    public static CustomAttributeBuilder NewInjectedFieldAttribute() {
        return new CustomAttributeBuilder(InjectedFieldAttributeCtor!, []);
    }
}
