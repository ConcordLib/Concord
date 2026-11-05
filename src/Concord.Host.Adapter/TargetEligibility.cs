using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;

namespace Concord.Host.Adapter;

/// <summary>The answer <see cref="TargetEligibility.Judge" /> gives for a target: whether a patch can be carried on it at all.</summary>
public enum TargetHandling {
    /// <summary>The composed body is published over the target, and callers already inlined or already jitted pick it up.</summary>
    Replace,

    /// <summary>Nothing can be staged over this target, so the caller surfaces <see cref="TargetVerdict.Code" /> and stops.</summary>
    Refuse,
}

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
    /// <returns>A <see cref="TargetHandling.Replace" /> verdict with empty code and message, or a refusal carrying <see cref="DynamicTargetCode" /> or <see cref="NoBodyCode" />.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target" /> is <c>null</c>.</exception>
    public static TargetVerdict Judge(MethodBase target) {
        ArgumentNullException.ThrowIfNull(target);

        if (target is DynamicMethod) {
            return new TargetVerdict(TargetHandling.Replace, string.Empty, string.Empty);
        }

        if (target.IsCollectible || target.Module.Assembly.IsDynamic) {
            return new TargetVerdict(
                TargetHandling.Refuse,
                DynamicTargetCode,
                $"{RefusalMarker} '{WaylithDetourBackend.Describe(target)}' is declared in " +
                $"'{target.Module.Name}', a dynamic or collectible module. A method in one is not composed on " +
                $"this build. {StillApplies}");
        }

        if (NoBody(target) is not string reason) {
            return new TargetVerdict(TargetHandling.Replace, string.Empty, string.Empty);
        }

        string remedy = target.IsAbstract
            ? "Patch an override or an implementation instead."
            : "Patch the managed code that calls it instead.";

        return new TargetVerdict(
            TargetHandling.Refuse,
            NoBodyCode,
            $"{RefusalMarker} '{WaylithDetourBackend.Describe(target)}' {reason}, so the runtime has no IL body " +
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

/// <summary>What <see cref="TargetEligibility" /> decided about one target, with the text a caller logs and throws on a refusal.</summary>
public sealed class TargetVerdict {
    internal TargetVerdict(TargetHandling handling, string code, string message) {
        Handling = handling;
        Code = code;
        Message = message;
    }

    /// <summary>The outcome reached for this target, which decides whether the caller publishes a body or stops.</summary>
    public TargetHandling Handling { get; }

    /// <summary>The Concord diagnostic code, or an empty string on a <see cref="TargetHandling.Replace" /> verdict.</summary>
    public string Code { get; }

    /// <summary>Why the verdict came out this way; empty on a clean <see cref="TargetHandling.Replace" /> and prefixed with <see cref="TargetEligibility.RefusalMarker" /> on a refusal.</summary>
    public string Message { get; }

    /// <summary>Raises the refusal as an exception carrying <see cref="Code" /> and <see cref="Message" />; a replace verdict passes through untouched.</summary>
    /// <exception cref="ConcordEmitException">The verdict is <see cref="TargetHandling.Refuse" />.</exception>
    public void ThrowIfRefused() {
        if (Handling == TargetHandling.Refuse) {
            throw new ConcordEmitException(Code, Message);
        }
    }
}
