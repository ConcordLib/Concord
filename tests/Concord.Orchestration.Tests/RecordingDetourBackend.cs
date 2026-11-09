using System.Reflection;
using Concord.Backend;
using Concord.Emit;

namespace Concord.Orchestration.Tests;

public sealed record AppliedInjection(MethodBase Target, Injection Injection);

public sealed class RecordingDetourBackend : IPatchBackend {
    private readonly List<AppliedInjection> applied = [];
    private readonly List<AppliedInjection> live = [];

    public IReadOnlyList<AppliedInjection> Applied {
        get {
            lock (applied) {
                return applied.ToArray();
            }
        }
    }

    public Func<MethodBase, Injection, Exception?>? FailOn { get; set; }

    public string Name => nameof(RecordingDetourBackend);

    public int PatchedTargetCount {
        get {
            lock (applied) {
                return live.Select(entry => entry.Target).Distinct().Count();
            }
        }
    }

    public TargetVerdict Judge(MethodBase target) {
        return TargetVerdict.Accept();
    }

    public bool OwnsBody(MethodBase target) {
        return false;
    }

    public bool Recompose(MethodBase target) {
        return false;
    }

    public IReadOnlyList<string> OwnersOf(MethodBase target) {
        List<string> owners = [];
        lock (applied) {
            foreach (AppliedInjection entry in live) {
                if (Equals(entry.Target, target) && !owners.Contains(entry.Injection.Owner)) {
                    owners.Add(entry.Injection.Owner);
                }
            }
        }

        return owners;
    }

    public IBackendHandle ApplyComposed(MethodBase target, IReadOnlyList<Injection> added) {
        foreach (Injection injection in added) {
            if (FailOn?.Invoke(target, injection) is Exception failure) {
                throw failure;
            }
        }

        AppliedInjection[] entries = added.Select(injection => new AppliedInjection(target, injection)).ToArray();
        lock (applied) {
            applied.AddRange(entries);
            live.AddRange(entries);
        }

        return new Handle(this, target, entries);
    }

    public IReadOnlyList<Injection> InjectionsOn(MethodBase target) {
        lock (applied) {
            return applied.Where(entry => Equals(entry.Target, target)).Select(entry => entry.Injection).ToArray();
        }
    }

    public IReadOnlyList<Injection> LiveInjectionsOn(MethodBase target) {
        lock (applied) {
            return live.Where(entry => Equals(entry.Target, target)).Select(entry => entry.Injection).ToArray();
        }
    }

    private void Drop(AppliedInjection[] entries) {
        lock (applied) {
            foreach (AppliedInjection entry in entries) {
                live.RemoveAll(candidate => ReferenceEquals(candidate, entry));
            }
        }
    }

    private sealed class Handle(RecordingDetourBackend backend, MethodBase original, AppliedInjection[] entries) : IBackendHandle {
        public MethodBase Original { get; } = original;

        public bool IsApplied { get; private set; } = true;

        public void Dispose() {
            if (!IsApplied) {
                return;
            }

            IsApplied = false;
            backend.Drop(entries);
        }
    }
}
