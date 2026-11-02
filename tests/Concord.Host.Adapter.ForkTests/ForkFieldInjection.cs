using System.Reflection;

namespace Concord.Host.Adapter.ForkTests;

public enum FieldInjectionGap {
    None,
    Runtime,
    Api,
    Disabled,
}

public static class ForkFieldInjection {
    public const string InjectionTypeName = "WaylithClr.FieldInjection, System.Private.CoreLib";

    public const string BatchTypeName = "WaylithClr.FieldInjectionBatch, System.Private.CoreLib";

    public const string ConfigKey = "WaylithClr.FieldInjection";

    private const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.Static;

    private const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.Instance;

    private static readonly Type? Injection = Type.GetType(InjectionTypeName, throwOnError: false);

    internal static readonly Type? BatchType = Type.GetType(BatchTypeName, throwOnError: false);

    private static readonly PropertyInfo? Supported = Injection?.GetProperty("IsSupported", AnyStatic);

    private static readonly PropertyInfo? Epoch = Injection?.GetProperty("LayoutEpoch", AnyStatic);

    private static readonly MethodInfo? Apply = Injection?.GetMethod("ApplyPending", AnyStatic, Type.EmptyTypes);

    private static readonly MethodInfo? Add = BatchType?.GetMethod(
        "AddField",
        AnyInstance,
        [typeof(Type), typeof(string), typeof(Type), typeof(FieldAttributes)]);

    private static readonly MethodInfo? CommitEntry = BatchType?.GetMethod("Commit", AnyInstance, Type.EmptyTypes);

    public static FieldInjectionGap Gap =>
        Injection is null || BatchType is null ? FieldInjectionGap.Runtime
        : Supported is null || Epoch is null || Apply is null || Add is null || CommitEntry is null ? FieldInjectionGap.Api
        : !IsSupported ? FieldInjectionGap.Disabled
        : FieldInjectionGap.None;

    public static bool Unavailable => Gap != FieldInjectionGap.None;

    public static bool IsSupported => Supported?.GetValue(null) is true;

    public static int LayoutEpoch => (int)Epoch!.GetValue(null)!;

    public static string Explain => Gap switch {
        FieldInjectionGap.Runtime =>
            $"'{InjectionTypeName}' is not present on '{RuntimeCapability.Runtime}', so no field can be injected here",
        FieldInjectionGap.Api =>
            $"'{InjectionTypeName}' is present on '{RuntimeCapability.Runtime}' but does not expose IsSupported, " +
            "LayoutEpoch, ApplyPending, AddField and Commit, so this is not the api these facts were written against",
        FieldInjectionGap.Disabled =>
            $"'{InjectionTypeName}'.IsSupported is false on '{RuntimeCapability.Runtime}'. Set the " +
            $"'{ConfigKey}' runtime config property to true, which is what the game's own runtimeconfig does",
        _ => $"'{InjectionTypeName}' reports IsSupported on '{RuntimeCapability.Runtime}'",
    };

    public static Batch NewBatch() {
        return new Batch(Activator.CreateInstance(BatchType!)!);
    }

    public static ApplyResult ApplyPending() {
        return new ApplyResult(Unwrapped(() => Apply!.Invoke(null, null))!);
    }

    internal static object? Unwrapped(Func<object?> call) {
        try {
            return call();
        } catch (TargetInvocationException thrown) when (thrown.InnerException is not null) {
            throw thrown.InnerException;
        }
    }

    public sealed class Batch {
        private readonly object inner;

        internal Batch(object inner) {
            this.inner = inner;
        }

        internal object Inner => inner;

        public string Status => Read("Status")!.ToString()!;

        public string LastRefusal => Read("LastRefusal")!.ToString()!;

        public string? PendingReason => (string?)Read("PendingReason");

        public Exception? Error => (Exception?)Read("Error");

        public long MigratedInstances => (long)Read("MigratedInstances")!;

        public string Verdict =>
            Status + (Error is { } failure ? ": " + failure.Message : string.Empty)
            + (LastRefusal == "None" ? string.Empty : " (" + LastRefusal + ": " + (PendingReason ?? "no reason given") + ")");

        public Injected AddField(Type declaringType, string name, Type fieldType) {
            return new Injected(Unwrapped(
                () => Add!.Invoke(inner, [declaringType, name, fieldType, FieldAttributes.Public]))!);
        }

        public void Commit() {
            Unwrapped(() => CommitEntry!.Invoke(inner, null));
        }

        private object? Read(string name) {
            return BatchType!.GetProperty(name, AnyInstance)!.GetValue(inner);
        }
    }

    public sealed class Injected {
        private readonly object inner;

        internal Injected(object inner) {
            this.inner = inner;
        }

        public FieldInfo? Field => (FieldInfo?)Read("Field");

        public int MetadataToken => (int)Read("MetadataToken")!;

        public string Name => (string)Read("Name")!;

        private object? Read(string name) {
            return inner.GetType().GetProperty(name, AnyInstance)!.GetValue(inner);
        }
    }

    public sealed class ApplyResult {
        private readonly object inner;

        internal ApplyResult(object inner) {
            this.inner = inner;
        }

        public bool AllApplied => (bool)Read("AllApplied")!;

        public long MigratedInstances => (long)Read("MigratedInstances")!;

        public IReadOnlyList<MethodBase> StaleDynamicMethods => (IReadOnlyList<MethodBase>)Read("StaleDynamicMethods")!;

        public IReadOnlyList<MethodBase> RejittedMethods => (IReadOnlyList<MethodBase>)Read("RejittedMethods")!;

        private object? Read(string name) {
            return inner.GetType().GetProperty(name, AnyInstance)!.GetValue(inner);
        }
    }
}
