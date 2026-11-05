using System.Reflection;
using Concord.Detour;
using Concord.Emit;

namespace Concord.Tests.Shared;

public sealed class RecordingDetourBackend : IDetourBackend {
    public List<(MethodBase Target, IReadOnlyList<Injection> Added)> ComposedDetours { get; } =
        new List<(MethodBase Target, IReadOnlyList<Injection> Added)>();

    public List<IDetourHandle> Handles { get; } = new List<IDetourHandle>();

    public int PatchedTargetCount => ComposedDetours.Select(entry => entry.Target).Distinct().Count();

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

    public IDetourHandle ApplyComposed(MethodBase target, IReadOnlyList<Injection> added) {
        ComposedDetours.Add((target, added));
        RecordingHandle handle = new RecordingHandle(target);
        Handles.Add(handle);
        return handle;
    }

    private sealed class RecordingHandle(MethodBase original) : IDetourHandle {
        public MethodBase Original { get; } = original;

        public bool IsApplied { get; private set; } = true;

        public void Dispose() {
            IsApplied = false;
        }
    }
}
