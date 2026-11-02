using System.Reflection;

namespace Concord.Host.Adapter;

/// <summary>
///     What the probe found when it asked this runtime whether a stale-method handler can be registered.
/// </summary>
public enum InjectionCapabilityGap {
    /// <summary>The api is present, enabled, and shaped as this build was written against.</summary>
    None,

    /// <summary>The injection type is not on this runtime.</summary>
    AssemblyAbsent,

    /// <summary>The type or its delegate exists but not with the members this build was written against.</summary>
    ApiUnrecognized,

    /// <summary>The api reports itself switched off.</summary>
    DisabledByRuntime,
}

/// <summary>
///     Answers whether this runtime's <c>Waylith.Injection.Injection</c> accepts a stale-method handler.
/// </summary>
public static class InjectionCapability {
    /// <summary>The assembly-qualified name of the runtime type this probe binds to.</summary>
    public const string InjectionTypeName = "Waylith.Injection.Injection, Waylith.Injection";

    /// <summary>The assembly-qualified name of the delegate <c>RegisterStaleMethodHandler</c> takes.</summary>
    public const string DelegateTypeName = "Waylith.Injection.HarmonyRegenerateDelegate, Waylith.Injection";

    private const string SupportFlag = "IsSupported";

    private const string RegisterEntry = "RegisterStaleMethodHandler";

    private const string RewrittenEntry = "HasRewrittenBody";

    private const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.Static;

    private static readonly (InjectionCapabilityGap Gap, string? Failure) Once = Probe(Injection(), Delegate());

    /// <summary>The one-shot verdict for this process.</summary>
    public static InjectionCapabilityGap Gap => Once.Gap;

    /// <summary>The exception detail captured while probing, set only for <see cref="InjectionCapabilityGap.ApiUnrecognized" />.</summary>
    public static string? ProbeFailure => Once.Failure;

    /// <summary>
    ///     <see langword="true" /> unless the api is present, enabled and shaped as expected.
    /// </summary>
    public static bool Unavailable => Gap != InjectionCapabilityGap.None;

    /// <summary>Looks the runtime's injection type up by name without throwing when it is absent.</summary>
    /// <returns>The injection type, or <see langword="null" />.</returns>
    public static Type? Injection() {
        return Type.GetType(InjectionTypeName, throwOnError: false);
    }

    /// <summary>Looks <see cref="DelegateTypeName" /> up by name without throwing when it is absent.</summary>
    /// <returns>The delegate type, or <see langword="null" />.</returns>
    public static Type? Delegate() {
        return Type.GetType(DelegateTypeName, throwOnError: false);
    }

    /// <summary>Judges a candidate injection type against a candidate delegate type.</summary>
    /// <param name="injection">The type to judge, or <see langword="null" /> to report the assembly absent.</param>
    /// <param name="handlerDelegate">The delegate type <c>RegisterStaleMethodHandler</c> should take.</param>
    /// <returns>The verdict, plus the exception detail when the probe threw.</returns>
    public static (InjectionCapabilityGap Gap, string? Failure) Probe(Type? injection, Type? handlerDelegate) {
        if (injection is null) {
            return (InjectionCapabilityGap.AssemblyAbsent, null);
        }

        object? flag;
        try {
            flag = injection.GetProperty(SupportFlag, AnyStatic)?.GetValue(null);
        } catch (Exception thrown) {
            Exception cause = (thrown as TargetInvocationException)?.InnerException ?? thrown;
            return (InjectionCapabilityGap.ApiUnrecognized, cause.GetType().Name + ": " + cause.Message);
        }

        if (flag is not bool supported) {
            return (InjectionCapabilityGap.ApiUnrecognized, null);
        }

        if (handlerDelegate is null
            || injection.GetMethod(RegisterEntry, AnyStatic, [handlerDelegate]) is null
            || injection.GetMethod(RewrittenEntry, AnyStatic, [typeof(MethodBase)])?.ReturnType != typeof(bool)) {
            return (InjectionCapabilityGap.ApiUnrecognized, null);
        }

        return (supported ? InjectionCapabilityGap.None : InjectionCapabilityGap.DisabledByRuntime, null);
    }

    /// <summary>Explains this process's verdict, including the probe failure detail when there is one.</summary>
    /// <returns>A log-ready sentence naming the api, this runtime, and which gap applies.</returns>
    public static string Explain() {
        return Explain(Gap, ProbeFailure);
    }

    /// <summary>Explains a verdict held without a probe failure detail.</summary>
    /// <param name="gap">The verdict to explain.</param>
    /// <returns>A log-ready sentence naming the api and this runtime.</returns>
    public static string Explain(InjectionCapabilityGap gap) {
        return Explain(gap, null);
    }

    /// <summary>Explains a verdict, folding in the detail captured when the probe threw.</summary>
    /// <param name="gap">The verdict to explain.</param>
    /// <param name="failure">The probe failure detail, read only for <see cref="InjectionCapabilityGap.ApiUnrecognized" />.</param>
    /// <returns>A log-ready sentence naming the api, this runtime, and which gap applies.</returns>
    /// <exception cref="NotSupportedException">The verdict is not a declared <see cref="InjectionCapabilityGap" /> value.</exception>
    public static string Explain(InjectionCapabilityGap gap, string? failure) {
        return gap switch {
            InjectionCapabilityGap.None =>
                $"'{InjectionTypeName}' reports {SupportFlag} on '{RuntimeCapability.Runtime}', so a " +
                "stale-method handler can be registered",
            InjectionCapabilityGap.AssemblyAbsent =>
                $"'{InjectionTypeName}' is not present on '{RuntimeCapability.Runtime}', so no stale-method " +
                "handler can be registered here",
            InjectionCapabilityGap.ApiUnrecognized =>
                $"'{InjectionTypeName}' is present on '{RuntimeCapability.Runtime}' but does not expose a " +
                $"static bool {SupportFlag} plus {RegisterEntry} taking '{DelegateTypeName}' and " +
                $"{RewrittenEntry} returning bool, so this is not the api this build was written against" +
                (failure is null ? string.Empty : ": " + failure),
            InjectionCapabilityGap.DisabledByRuntime =>
                $"'{InjectionTypeName}'.{SupportFlag} is false on '{RuntimeCapability.Runtime}', so the " +
                "stale-method channel stays unused",
            _ => throw new NotSupportedException(gap + " has no explanation"),
        };
    }
}
