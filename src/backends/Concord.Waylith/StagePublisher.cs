using System.Collections.Concurrent;
using System.Reflection;
using Concord.Backend;

namespace Concord.Waylith;

/// <summary>
///     Holds the ordered injections each staged target carries, and rebuilds that target through the
///     transformer pipeline. A publish writes the record first, then asks for the rebuild.
/// </summary>
public sealed class StagePublisher : ITargetBodyOwner {
    /// <summary>Log prefix for a target the stage refused or failed on, so no patch on it is live.</summary>
    public const string RefusedMarker = "[Concord.Waylith] stage-refused";

    /// <summary>The diagnostic code on the exception a refused or failed target raises.</summary>
    public const string RefusedCode = "CONC179";

    private readonly ConcurrentDictionary<(MethodBase Target, string Message), byte> announced = new();

    private readonly Func<MethodBase, RuntimeMethodHandle> handleOf;

    private readonly Action<string> log;

    private readonly ConcurrentDictionary<RuntimeMethodHandle, StagedTarget> records = new();

    private Func<RuntimeMethodHandle, Exception?> rebuild = static _ => NotWired();

    /// <summary>Initializes a new instance.</summary>
    /// <param name="handleOf">Resolves a target to the definition handle the pipeline keys a stage by.</param>
    /// <param name="log">Where the one line about a refused or failed target goes.</param>
    /// <exception cref="ArgumentNullException">Either argument is <c>null</c>.</exception>
    public StagePublisher(Func<MethodBase, RuntimeMethodHandle> handleOf, Action<string> log) {
        ArgumentNullException.ThrowIfNull(handleOf);
        ArgumentNullException.ThrowIfNull(log);
        this.handleOf = handleOf;
        this.log = log;
    }

    /// <inheritdoc />
    public int PublishedTargetCount {
        get {
            int count = 0;
            foreach (KeyValuePair<RuntimeMethodHandle, StagedTarget> entry in records) {
                if (entry.Value.Failure is null) {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>Sets the rebuild every publish on this publisher calls.</summary>
    /// <param name="pipelineRebuild">Rebuilds one definition handle, returning the transform's exception or <c>null</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pipelineRebuild" /> is <c>null</c>.</exception>
    public void UseRebuild(Func<RuntimeMethodHandle, Exception?> pipelineRebuild) {
        ArgumentNullException.ThrowIfNull(pipelineRebuild);
        rebuild = pipelineRebuild;
    }

    /// <inheritdoc />
    public bool PublishComposed(MethodBase target, IReadOnlyList<Injection> ordered) {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(ordered);

        RuntimeMethodHandle handle = handleOf(target);
        records[handle] = new StagedTarget(target, ordered, null);
        return Rebuilt(handle, target) is null;
    }

    /// <inheritdoc />
    /// <exception cref="ConcordEmitException">The rebuild back onto the original body failed, carrying <see cref="RefusedCode" />.</exception>
    public void RevertToOriginal(MethodBase target) {
        ArgumentNullException.ThrowIfNull(target);

        RuntimeMethodHandle handle = handleOf(target);
        records.TryRemove(handle, out _);

        FrameAttribution.Forget(target);
        if (Rebuilt(handle, target) is Exception failure) {
            throw Refusal(target, failure);
        }
    }

    /// <inheritdoc />
    public bool IsPublished(MethodBase target) {
        ArgumentNullException.ThrowIfNull(target);
        return Staged(handleOf(target)) is { Failure: null };
    }

    /// <inheritdoc />
    public bool Rebuild(MethodBase target) {
        ArgumentNullException.ThrowIfNull(target);

        RuntimeMethodHandle handle = handleOf(target);
        if (Staged(handle) is null) {
            return false;
        }

        return Rebuilt(handle, target) is null;
    }

    internal StagedTarget? Staged(RuntimeMethodHandle handle) {
        return records.TryGetValue(handle, out StagedTarget? staged) ? staged : null;
    }

    internal void Report(string message) {
        log(message);
    }

    internal void Failed(RuntimeMethodHandle handle, Exception failure) {
        if (Staged(handle) is not StagedTarget staged) {
            return;
        }

        records[handle] = new StagedTarget(staged.Target, staged.Ordered, failure);
        Announce(staged.Target, failure);
    }

    internal void DefaultBodiesChanged(ReadOnlySpan<RuntimeMethodHandle> methods) {
        foreach (RuntimeMethodHandle handle in methods) {
            if (Staged(handle) is StagedTarget staged) {
                FrameAttribution.Forget(staged.Target);
            }
        }
    }

    private static Exception NotWired() {
        return new InvalidOperationException(
            "No transformer pipeline is wired into this " + nameof(StagePublisher) + ", so a staged body has " +
            "nothing to rebuild it. " + nameof(UseRebuild) + " has to run before the first publish.");
    }

    private Exception? Rebuilt(RuntimeMethodHandle handle, MethodBase target) {
        Exception? failure;
        try {
            failure = rebuild(handle);
        } catch (Exception thrown) when (thrown is not OutOfMemoryException) {
            failure = thrown;
        }

        if (failure is null) {
            if (Staged(handle) is { Failure: not null } healed) {
                records[handle] = new StagedTarget(healed.Target, healed.Ordered, null);
            }

            return null;
        }

        if (Staged(handle) is StagedTarget staged) {
            records[handle] = new StagedTarget(staged.Target, staged.Ordered, failure);
        }

        Announce(target, failure);
        return failure;
    }

    private void Announce(MethodBase target, Exception failure) {
        if (!announced.TryAdd((target, failure.Message), 0)) {
            return;
        }

        log(
            RefusedMarker + " '" + WaylithClrBackend.Describe(target) + "': " + failure.GetType().Name + ", " +
            failure.Message.TrimEnd('.') + ". Its composed body was not staged, so the method keeps running the " +
            "code it was declared with and every patch on it is refused.");
    }

    private ConcordEmitException Refusal(MethodBase target, Exception failure) {
        return new ConcordEmitException(
            RefusedCode,
            "'" + WaylithClrBackend.Describe(target) + "' could not be staged through the transformer " +
            "pipeline: " + failure.GetType().Name + ", " + failure.Message.TrimEnd('.') +
            ". The method keeps the body it was declared with.");
    }
}

internal sealed class StagedTarget {
    internal StagedTarget(MethodBase target, IReadOnlyList<Injection> ordered, Exception? failure) {
        Target = target;
        Ordered = ordered;
        Failure = failure;
    }

    internal MethodBase Target { get; }

    internal IReadOnlyList<Injection> Ordered { get; }

    internal Exception? Failure { get; }
}
