using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using Concord.Detour;

namespace Concord.Host.Adapter;

/// <summary>
///     Owns the one body a patched target has. A target carries either the composed body or its original,
///     never both, so a caller that changes the live injection set publishes the whole set again.
/// </summary>
public interface ITargetBodyOwner {
    /// <summary>How many targets currently carry a live body from this owner.</summary>
    int PublishedTargetCount { get; }

    /// <summary>
    ///     Installs the composed body for the whole live injection set, not just the new ones.
    ///     <see cref="StagePublisher" /> is the implementation.
    /// </summary>
    /// <param name="target">The method whose body is replaced.</param>
    /// <param name="ordered">Every injection that must run, already in composition order.</param>
    /// <returns><see langword="true" /> when the owner took the body, <see langword="false" /> when it refused it.</returns>
    bool PublishComposed(MethodBase target, IReadOnlyList<Injection> ordered);

    /// <summary>Puts <paramref name="target" />'s own body back, leaving it with no injections.</summary>
    /// <param name="target">The method to restore.</param>
    void RevertToOriginal(MethodBase target);

    /// <summary>Reports whether <paramref name="target" /> currently carries a live body from this owner.</summary>
    /// <param name="target">The method to check.</param>
    /// <returns><see langword="false" /> for a target never published, and for one whose last publish failed.</returns>
    bool IsPublished(MethodBase target);

    /// <summary>Rebuilds <paramref name="target" />'s body from the injections already recorded for it.</summary>
    /// <param name="target">The method to rebuild.</param>
    /// <returns><see langword="false" /> when nothing is recorded for it, or the rebuild failed.</returns>
    bool Rebuild(MethodBase target);
}

/// <summary>
///     One entry per patched target, each holding that target's live injections. The markers below are the
///     log prefixes a caller greps for when a change to a target did not take.
/// </summary>
public sealed class WaylithTargetStack {
    /// <summary>Log prefix for a removal that could not be published, so the old injections keep running.</summary>
    public const string UndoFailedMarker = "[Concord.Waylith] undo-failed";

    /// <summary>Log prefix for the worse case: the rollback after a failure could not be published either.</summary>
    public const string RepublishFailedMarker = "[Concord.Waylith] rollback-republish-failed";

    private readonly ConcurrentDictionary<MethodBase, TargetEntry> entries = new ConcurrentDictionary<MethodBase, TargetEntry>();

    private readonly ITargetBodyOwner bodyOwner;

    private readonly Action<string> log;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="bodyOwner">The owner that writes and reverts a target's body.</param>
    /// <param name="log">Sink for the failure reports this stack cannot throw, since undo runs from Dispose.</param>
    public WaylithTargetStack(ITargetBodyOwner bodyOwner, Action<string> log) {
        ArgumentNullException.ThrowIfNull(bodyOwner);
        ArgumentNullException.ThrowIfNull(log);
        this.bodyOwner = bodyOwner;
        this.log = log;
    }

    /// <summary>Adds injections to <paramref name="target" /> and republishes it with the full live set.</summary>
    /// <param name="target">The method to patch.</param>
    /// <param name="added">The injections this call contributes.</param>
    /// <returns>A handle owning only <paramref name="added" />; an empty list yields a handle that applied nothing.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null" />.</exception>
    public WaylithDetourHandle Add(MethodBase target, IReadOnlyList<Injection> added) {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(added);

        MethodBase key = Key(target);
        TargetEntry entry = entries.GetOrAdd(
            key,
            static (method, stack) => new TargetEntry(method, stack.bodyOwner, stack.log),
            this);

        return entry.Add(target, WrapperComposer.TagRequestedInstantiation(target, added));
    }

    /// <summary>Reports whether <paramref name="target" /> currently carries a composed body.</summary>
    /// <param name="target">The method to check.</param>
    /// <returns><see langword="false" /> for a target never patched here, and once its last injection is gone.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target" /> is <see langword="null" />.</exception>
    public bool IsPublished(MethodBase target) {
        ArgumentNullException.ThrowIfNull(target);
        return entries.TryGetValue(Key(target), out TargetEntry? entry) && entry.IsPublished;
    }

    /// <summary>Lists the owners with a live injection on <paramref name="target" />, in run order.</summary>
    /// <param name="target">The method to inspect.</param>
    /// <returns>Distinct owner ids in composition order, empty for a target this stack has nothing on.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target" /> is <see langword="null" />.</exception>
    public IReadOnlyList<string> OwnersOf(MethodBase target) {
        ArgumentNullException.ThrowIfNull(target);
        return entries.TryGetValue(Key(target), out TargetEntry? entry) ? entry.Owners() : [];
    }

    private static MethodBase Key(MethodBase target) {
        return BodyScope.KeyOf(target);
    }
}

/// <summary>
///     The handle a caller holds over the injections one <see cref="WaylithTargetStack.Add" /> contributed.
///     Disposing removes those and no others, so other owners' injections on the same target keep running.
/// </summary>
public sealed class WaylithDetourHandle : IDetourHandle {
    private readonly TargetEntry entry;

    private readonly IReadOnlyList<long> owned;

    private int removed;

    internal WaylithDetourHandle(TargetEntry entry, MethodBase original, IReadOnlyList<long> owned) {
        this.entry = entry;
        this.owned = owned;
        Original = original;
    }

    /// <summary>The method this handle was created for; the stack may key its body under a different one.</summary>
    public MethodBase Original { get; }

    /// <summary>
    ///     <see langword="true" /> while this handle's injections are live. It also reads
    ///     <see langword="false" /> after another owner's change left the target with no body.
    /// </summary>
    public bool IsApplied => owned.Count > 0 && Volatile.Read(ref removed) == 0 && entry.IsPublished;

    /// <summary><see langword="null" /> until an undo fails; a value here means the injections still run.</summary>
    public Exception? UndoFailure { get; private set; }

    /// <summary>
    ///     Removes this handle's injections and republishes the target. Failures land in
    ///     <see cref="UndoFailure" /> rather than throwing, and a failed undo can be retried.
    /// </summary>
    public void Dispose() {
        if (owned.Count == 0 || Interlocked.Exchange(ref removed, 1) != 0) {
            return;
        }

        if (Environment.HasShutdownStarted) {
            return;
        }

        Exception? failure;
        try {
            failure = entry.Remove(owned);
        } catch (Exception thrown) {
            failure = thrown;
        }

        UndoFailure = failure;
        if (failure is not null) {
            Volatile.Write(ref removed, 0);
        }
    }
}

internal sealed class TargetEntry {
    private readonly MethodBase key;

    private readonly ITargetBodyOwner bodyOwner;

    private readonly Action<string> log;

    private readonly object gate = new object();

    private readonly List<(long Seq, Injection Injection)> live = [];

    private long sequence;

    internal TargetEntry(MethodBase key, ITargetBodyOwner bodyOwner, Action<string> log) {
        this.key = key;
        this.bodyOwner = bodyOwner;
        this.log = log;
    }

    internal bool IsPublished {
        get {
            lock (gate) {
                return live.Count > 0 && bodyOwner.IsPublished(key);
            }
        }
    }

    internal IReadOnlyList<string> Owners() {
        lock (gate) {
            return WaylithDetourBackend.DistinctOwners(InjectionOrderer.OrderForComposition(live));
        }
    }

    internal WaylithDetourHandle Add(MethodBase requested, IReadOnlyList<Injection> added) {
        if (added.Count == 0) {
            return new WaylithDetourHandle(this, requested, []);
        }

        lock (gate) {
            List<(long Seq, Injection Injection)> previous = new List<(long Seq, Injection Injection)>(live);
            long previousSequence = sequence;
            long[] owned = new long[added.Count];
            for (int i = 0; i < added.Count; i++) {
                owned[i] = sequence++;
                live.Add((owned[i], added[i]));
            }

            bool published;
            try {
                published = PublishLive();
            } catch {
                if (Rollback(previous, previousSequence) is string report) {
                    log(report);
                }

                throw;
            }

            if (!published) {
                if (Rollback(previous, previousSequence) is string report) {
                    log(report);
                }

                return new WaylithDetourHandle(this, requested, []);
            }

            return new WaylithDetourHandle(this, requested, owned);
        }
    }

    internal Exception? Remove(IReadOnlyList<long> owned) {
        lock (gate) {
            List<(long Seq, Injection Injection)> previous = new List<(long Seq, Injection Injection)>(live);
            foreach (long seq in owned) {
                for (int i = live.Count - 1; i >= 0; i--) {
                    if (live[i].Seq == seq) {
                        live.RemoveAt(i);
                        break;
                    }
                }
            }

            bool published;
            try {
                published = PublishLive();
            } catch (Exception failure) {
                return FailRemoval(previous, failure);
            }

            if (!published) {
                return FailRemoval(previous, new InvalidOperationException("the stage refused the remaining set"));
            }

            return null;
        }
    }

    private Exception FailRemoval(List<(long Seq, Injection Injection)> previous, Exception failure) {
        string? report = Rollback(previous, sequence);
        log(WaylithTargetStack.UndoFailedMarker + " '" + WaylithDetourBackend.Describe(key) + "': " +
            failure.GetType().Name + ", " + failure.Message.TrimEnd('.') +
            ". Its injections keep running until the next patch change on that target.");
        if (report is not null) {
            log(report);
        }

        return failure;
    }

    private string? Rollback(List<(long Seq, Injection Injection)> previous, long previousSequence) {
        live.Clear();
        live.AddRange(previous);
        sequence = previousSequence;

        bool published;
        try {
            published = PublishLive();
        } catch (Exception failure) when (failure is not OutOfMemoryException) {
            return WaylithTargetStack.RepublishFailedMarker + " '" + WaylithDetourBackend.Describe(key) + "': " +
                failure.GetType().Name + ", " + failure.Message.TrimEnd('.') +
                ". The installed body no longer matches the injections this target is recorded as carrying.";
        }

        if (!published) {
            return WaylithTargetStack.RepublishFailedMarker + " '" + WaylithDetourBackend.Describe(key) +
                "': the stage refused the republished set." +
                " The installed body no longer matches the injections this target is recorded as carrying.";
        }

        return null;
    }

    private bool PublishLive() {
        if (live.Count == 0) {
            bodyOwner.RevertToOriginal(key);
            return true;
        }

        return bodyOwner.PublishComposed(key, InjectionOrderer.OrderForComposition(live));
    }
}
