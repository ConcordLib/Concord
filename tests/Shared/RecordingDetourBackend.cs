using System.Reflection;
using Concord.Backend;
using Concord.Emit;

namespace Concord.Tests.Shared;

public sealed class RecordingDetourBackend : IPatchBackend {
    public List<(MethodBase Target, IReadOnlyList<Injection> Added)> ComposedDetours { get; } =
        new List<(MethodBase Target, IReadOnlyList<Injection> Added)>();

    public List<IBackendHandle> Handles { get; } = new List<IBackendHandle>();

    public string Name => nameof(RecordingDetourBackend);

    public int PatchedTargetCount => ComposedDetours.Select(entry => entry.Target).Distinct().Count();

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
        List<string> owners = new List<string>();
        foreach ((MethodBase Target, IReadOnlyList<Injection> Added) entry in ComposedDetours) {
            if (!Equals(entry.Target, target)) {
                continue;
            }

            foreach (Injection injection in entry.Added) {
                if (!owners.Contains(injection.Owner)) {
                    owners.Add(injection.Owner);
                }
            }
        }

        return owners;
    }

    public IBackendHandle ApplyComposed(MethodBase target, IReadOnlyList<Injection> added) {
        ComposedDetours.Add((target, added));
        RecordingHandle handle = new RecordingHandle(target);
        Handles.Add(handle);
        return handle;
    }

    private sealed class RecordingHandle(MethodBase original) : IBackendHandle {
        public MethodBase Original { get; } = original;

        public bool IsApplied { get; private set; } = true;

        public void Dispose() {
            IsApplied = false;
        }
    }
}
