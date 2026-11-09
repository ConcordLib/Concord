using System.Reflection;
using Concord.Backend;
using Concord.Emit;

namespace Concord.MonoMod.Tests;

public sealed class FakeDetourBackend : IPatchBackend {
    public List<(MethodBase Original, MethodInfo Replacement)> AppliedDetours { get; } =
        new List<(MethodBase Original, MethodInfo Replacement)>();

    public List<(MethodBase Target, IReadOnlyList<Injection> Added)> ComposedDetours { get; } =
        new List<(MethodBase Target, IReadOnlyList<Injection> Added)>();

    public string Name => "Fake";

    public int PatchedTargetCount => ComposedDetours.Count + AppliedDetours.Count;

    public TargetVerdict Judge(MethodBase target) {
        return TargetVerdict.Accept();
    }

    public IBackendHandle Apply(MethodBase original, MethodInfo replacement) {
        AppliedDetours.Add((original, replacement));
        return new FakeHandle(original);
    }

    public IBackendHandle ApplyComposed(MethodBase target, IReadOnlyList<Injection> added) {
        ComposedDetours.Add((target, added));
        return new FakeHandle(target);
    }

    public IReadOnlyList<string> OwnersOf(MethodBase target) {
        List<string> owners = new List<string>();
        foreach ((MethodBase composedTarget, IReadOnlyList<Injection> added) in ComposedDetours) {
            if (composedTarget == target) {
                foreach (Injection injection in added) {
                    owners.Add(injection.Owner);
                }
            }
        }

        return owners;
    }

    public bool OwnsBody(MethodBase target) {
        foreach ((MethodBase composedTarget, _) in ComposedDetours) {
            if (composedTarget == target) {
                return true;
            }
        }

        return false;
    }

    public bool Recompose(MethodBase target) {
        return false;
    }

    private sealed class FakeHandle(MethodBase original) : IBackendHandle {
        public MethodBase Original { get; } = original;
        public bool IsApplied { get; private set; } = true;

        public void Dispose() {
            IsApplied = false;
        }
    }
}
