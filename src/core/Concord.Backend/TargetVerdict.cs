using Concord.Emit;

namespace Concord.Backend;

/// <summary>What <see cref="IPatchBackend.Judge" /> decided about one target, with the text a caller logs and throws on a refusal.</summary>
public sealed class TargetVerdict {
    private TargetVerdict(TargetHandling handling, string code, string message) {
        Handling = handling;
        Code = code;
        Message = message;
    }

    /// <summary>The outcome reached for this target, which decides whether the caller publishes a body or stops.</summary>
    public TargetHandling Handling { get; }

    /// <summary>The Concord diagnostic code on a refusal.</summary>
    public string Code { get; }

    /// <summary>Why the verdict came out this way; empty on a clean <see cref="TargetHandling.Accept" /> and prefixed with the backend's refusal marker on a refusal.</summary>
    public string Message { get; }

    /// <summary>The verdict for a target a backend can publish a composed body over.</summary>
    /// <returns>An accepting verdict with an empty code and message.</returns>
    public static TargetVerdict Accept() {
        return new TargetVerdict(TargetHandling.Accept, string.Empty, string.Empty);
    }

    /// <summary>The verdict for a target no backend can publish a body over.</summary>
    /// <param name="code">The Concord diagnostic code the caller surfaces.</param>
    /// <param name="message">Why the target was refused, for the log and the exception.</param>
    /// <returns>A refusing verdict carrying <paramref name="code" /> and <paramref name="message" />.</returns>
    public static TargetVerdict Refuse(string code, string message) {
        return new TargetVerdict(TargetHandling.Refuse, code, message);
    }

    /// <summary>Raises the refusal as an exception carrying <see cref="Code" /> and <see cref="Message" />; an accepting verdict passes through untouched.</summary>
    /// <exception cref="ConcordEmitException">The verdict is <see cref="TargetHandling.Refuse" />.</exception>
    public void ThrowIfRefused() {
        if (Handling == TargetHandling.Refuse) {
            throw new ConcordEmitException(Code, Message);
        }
    }
}
