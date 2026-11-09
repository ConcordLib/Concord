namespace Concord.Backend;

/// <summary>The answer a backend gives for a target: whether a patch can be carried on it at all.</summary>
public enum TargetHandling {
    /// <summary>The composed body is published over the target, and callers already inlined or already jitted pick it up.</summary>
    Accept,

    /// <summary>Nothing can be staged over this target, so the caller surfaces <see cref="TargetVerdict.Code" /> and stops.</summary>
    Refuse,
}
