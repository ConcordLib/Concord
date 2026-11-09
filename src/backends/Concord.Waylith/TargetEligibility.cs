using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using Concord.Backend;

namespace Concord.Waylith;

/// <summary>Where a caller asks whether a target can take a replaced body, and where it gets the message to log when it cannot.</summary>
public static class TargetEligibility {
    /// <summary>The diagnostic code on a refusal, raised when the target has no IL body for any backend to work with.</summary>
    public const string NoBodyCode = "CONC168";

    /// <summary>The diagnostic code on a refusal, raised when the target is declared in a collectible or dynamic module this build composes no body for.</summary>
    public const string DynamicTargetCode = StagePublisher.RefusedCode;

    /// <summary>The prefix on a message about a target no backend can patch, so a log reader can tell it from a patch failure.</summary>
    public const string RefusalMarker = "[Concord.Waylith] target-refused";

    private const string StillApplies =
        "Body replacement is the only way Concord patches anything, so every patch on this target is refused " +
        "and the method keeps the body it was declared with.";

    private static readonly ConcurrentDictionary<MethodBase, byte> Reported = new();

    /// <summary>Checks a target for the two things that rule body replacement out up front: a collectible or dynamic module, and no IL body at all.</summary>
    /// <param name="target">The method the caller wants to patch.</param>
    /// <returns>A <see cref="TargetHandling.Accept" /> verdict with empty code and message, or a refusal carrying <see cref="DynamicTargetCode" /> or <see cref="NoBodyCode" />.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target" /> is <c>null</c>.</exception>
    public static TargetVerdict Judge(MethodBase target) {
        ArgumentNullException.ThrowIfNull(target);

        if (target is DynamicMethod) {
            return TargetVerdict.Accept();
        }

        if (target.IsCollectible || target.Module.Assembly.IsDynamic) {
            return TargetVerdict.Refuse(
                DynamicTargetCode,
                $"{RefusalMarker} '{WaylithClrBackend.Describe(target)}' is declared in " +
                $"'{target.Module.Name}', a dynamic or collectible module. A method in one is not composed on " +
                $"this build. {StillApplies}");
        }

        if (NoBody(target) is not string reason) {
            return TargetVerdict.Accept();
        }

        string remedy = target.IsAbstract
            ? "Patch an override or an implementation instead."
            : "Patch the managed code that calls it instead.";

        return TargetVerdict.Refuse(
            NoBodyCode,
            $"{RefusalMarker} '{WaylithClrBackend.Describe(target)}' {reason}, so the runtime has no IL body " +
            $"to replace and no Concord backend can patch it. {remedy}");
    }

    /// <summary>Gate for logging a refusal, so a target refused on every patch attempt is only announced the first time in a process.</summary>
    /// <param name="target">The method about to be reported.</param>
    /// <returns><c>true</c> the first time it is called for a target, and <c>false</c> on every call after that.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target" /> is <c>null</c>.</exception>
    public static bool ReportOnce(MethodBase target) {
        ArgumentNullException.ThrowIfNull(target);
        return Reported.TryAdd(target, 0);
    }

    private static string? NoBody(MethodBase target) {
        if (target.GetMethodBody() is not null) {
            return null;
        }

        if (target.IsAbstract) {
            return "is abstract";
        }

        if ((target.Attributes & MethodAttributes.PinvokeImpl) != 0) {
            return "is a p/invoke declaration";
        }

        MethodImplAttributes implementation = target.MethodImplementationFlags;
        if ((implementation & MethodImplAttributes.InternalCall) != 0) {
            return "is an InternalCall intrinsic";
        }

        if ((implementation & MethodImplAttributes.CodeTypeMask) == MethodImplAttributes.Runtime) {
            return "is provided by the runtime, not by IL";
        }

        return "has no IL body";
    }
}
