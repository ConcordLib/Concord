using System.Reflection;
using Concord.Backend;
using Concord.Emit;
using HarmonyLib;

namespace Concord.HarmonyX;

/// <summary>Puts Concord's composed bodies on live methods on a host that runs HarmonyX.</summary>
public sealed class HarmonyXBackend : IPatchBackend {
    /// <summary>The name this backend reports in a log line, matching its package id.</summary>
    public const string BackendName = "Concord.HarmonyX";

    /// <summary>The owner id this backend reports for HarmonyX's own patches on a target.</summary>
    public const string ForeignOwner = "harmonyx";

    private const string RefusalCode = "CONC184";

    private static Action<string> log = static _ => { };

    /// <summary>Where this backend writes its log lines. A host points this at its own logger.</summary>
    public static Action<string> Log {
        get => log;
        set => log = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <inheritdoc />
    public string Name => BackendName;

    /// <inheritdoc />
    public int PatchedTargetCount => HarmonyXTarget.PatchedTargetCount;

    /// <inheritdoc />
    public TargetVerdict Judge(MethodBase target) {
        if (target is null) {
            throw new ArgumentNullException(nameof(target));
        }

        if (target.IsAbstract) {
            return Refuse(target, "it is abstract, so it has no body to compose onto");
        }

        if ((target.Attributes & MethodAttributes.PinvokeImpl) != 0) {
            return Refuse(target, "it is a p/invoke, whose body is native code outside the runtime");
        }

        MethodImplAttributes implementation = Implementation(target);
        if ((implementation & MethodImplAttributes.InternalCall) != 0) {
            return Refuse(target, "it is an internal call implemented inside the runtime, which has no IL body");
        }

        if ((implementation & MethodImplAttributes.CodeTypeMask) != MethodImplAttributes.IL) {
            return Refuse(target, "its body is not IL, so Concord cannot read or rewrite it");
        }

        if (!HasIlBody(target)) {
            return Refuse(target, "the runtime hands back no IL body for it");
        }

        return TargetVerdict.Accept();
    }

    /// <inheritdoc />
    public IBackendHandle ApplyComposed(MethodBase target, IReadOnlyList<Injection> added) {
        if (target is null) {
            throw new ArgumentNullException(nameof(target));
        }

        if (added is null) {
            throw new ArgumentNullException(nameof(added));
        }

        return HarmonyXTarget.Add(target, added);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> OwnersOf(MethodBase target) {
        if (target is null) {
            throw new ArgumentNullException(nameof(target));
        }

        List<string> result = new List<string>(HarmonyXTarget.OwnersFor(target));
        if (HasForeignPatch(target)) {
            result.Add(ForeignOwner);
        }

        return result;
    }

    /// <inheritdoc />
    public bool OwnsBody(MethodBase target) {
        if (target is null) {
            throw new ArgumentNullException(nameof(target));
        }

        return HarmonyXTarget.OwnsBody(target);
    }

    /// <inheritdoc />
    public bool Recompose(MethodBase target) {
        if (target is null) {
            throw new ArgumentNullException(nameof(target));
        }

        return HarmonyXTarget.RecomposeLive(target);
    }

    internal static void Write(string line) {
        log(line);
    }

    internal static string Describe(MethodBase target) {
        return (target.DeclaringType?.FullName ?? "<no declaring type>") + "." + target.Name;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A runtime that refuses to describe a method's implementation flags is reporting that it has no readable body, which is the refusal this check already produces.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2221:Exception should not be caught", Justification = "A runtime that refuses to describe a method's implementation flags is reporting that it has no readable body, which is the refusal this check already produces.")]
    private static MethodImplAttributes Implementation(MethodBase target) {
        try {
            return target.GetMethodImplementationFlags();
        } catch (Exception) {
            return MethodImplAttributes.InternalCall;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Reading a method body throws rather than returning null on several runtimes, and both outcomes mean the same thing here: there is no IL to compose onto.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2221:Exception should not be caught", Justification = "Reading a method body throws rather than returning null on several runtimes, and both outcomes mean the same thing here: there is no IL to compose onto.")]
    private static bool HasIlBody(MethodBase target) {
        try {
            return target.GetMethodBody() is not null;
        } catch (Exception) {
            return false;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Harmony's patch index is read for a log line only, so a host whose copy cannot answer reports no foreign owner rather than failing the caller.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2221:Exception should not be caught", Justification = "Harmony's patch index is read for a log line only, so a host whose copy cannot answer reports no foreign owner rather than failing the caller.")]
    private static bool HasForeignPatch(MethodBase target) {
        try {
            return Harmony.GetPatchInfo(target) is Patches patches && patches.Owners.Count > 0;
        } catch (Exception) {
            return false;
        }
    }

    private static TargetVerdict Refuse(MethodBase target, string because) {
        return TargetVerdict.Refuse(
            RefusalCode,
            $"[{BackendName}] refused '{Describe(target)}': {because}.");
    }
}
