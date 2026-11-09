using System.Reflection;
using System.Threading;
using Concord.Backend;
using Concord.Emit;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Concord.HarmonyX;

internal sealed class HarmonyXTarget {
    internal const string HookId = "Concord";

    private const string FailedMarker = "[Concord.HarmonyX] compose-failed";

    private static readonly Dictionary<MethodBase, HarmonyXTarget> Targets = new Dictionary<MethodBase, HarmonyXTarget>();

    private static readonly object TargetsGate = new object();

    private readonly MethodBase target;

    private readonly object gate = new object();

    private readonly List<(long Seq, Injection Injection)> live = new List<(long Seq, Injection Injection)>();

    private ILHook? hook;

    private long sequence;

    private IReadOnlyList<string> owners = Array.Empty<string>();

    private IReadOnlyList<Injection> ordered = Array.Empty<Injection>();

    private bool composeFailed;

    private HarmonyXTarget(MethodBase target) {
        this.target = target;
    }

    internal static int PatchedTargetCount {
        get {
            List<HarmonyXTarget> all;
            lock (TargetsGate) {
                all = new List<HarmonyXTarget>(Targets.Values);
            }

            int patched = 0;
            foreach (HarmonyXTarget entry in all) {
                if (entry.IsLive) {
                    patched++;
                }
            }

            return patched;
        }
    }

    private bool IsLive {
        get {
            lock (gate) {
                return hook is { IsApplied: true } && !composeFailed;
            }
        }
    }

    internal static IBackendHandle Add(MethodBase target, IReadOnlyList<Injection> added) {
        MethodBase key = MethodIdentity.SharedBodyKey(target);
        added = WrapperComposer.TagRequestedInstantiation(target, added);

        HarmonyXTarget entry;
        lock (TargetsGate) {
            if (!Targets.TryGetValue(key, out entry!)) {
                entry = new HarmonyXTarget(key);
                Targets[key] = entry;
            }
        }

        return entry.AddInternal(added);
    }

    internal static IReadOnlyList<string> OwnersFor(MethodBase target) {
        HarmonyXTarget? entry = Find(target);
        if (entry is null) {
            return Array.Empty<string>();
        }

        lock (entry.gate) {
            return entry.owners;
        }
    }

    internal static bool OwnsBody(MethodBase target) {
        return Find(target) is { IsLive: true };
    }

    internal static bool RecomposeLive(MethodBase target) {
        HarmonyXTarget? entry = Find(target);
        if (entry is null) {
            return false;
        }

        lock (entry.gate) {
            if (entry.live.Count == 0 || entry.hook is null) {
                return false;
            }

            entry.Rearm();
            return !entry.composeFailed;
        }
    }

    private static HarmonyXTarget? Find(MethodBase target) {
        MethodBase key = MethodIdentity.SharedBodyKey(target);
        lock (TargetsGate) {
            return Targets.TryGetValue(key, out HarmonyXTarget? entry) ? entry : null;
        }
    }

    private static List<string> BuildOwners(IReadOnlyList<Injection> ordered) {
        List<string> result = new List<string>(ordered.Count);
        for (int i = ordered.Count - 1; i >= 0; i--) {
            string owner = ordered[i].Owner;
            if (!result.Contains(owner)) {
                result.Add(owner);
            }
        }

        return result;
    }

    private IBackendHandle AddInternal(IReadOnlyList<Injection> added) {
        lock (gate) {
            List<long> owned = new List<long>(added.Count);
            foreach (Injection injection in added) {
                long seq = sequence++;
                live.Add((seq, injection));
                owned.Add(seq);
            }

            Rearm();
            return new Handle(this, owned);
        }
    }

    private void Remove(List<long> owned) {
        lock (gate) {
            foreach (long seq in owned) {
                for (int i = live.Count - 1; i >= 0; i--) {
                    if (live[i].Seq == seq) {
                        live.RemoveAt(i);
                    }
                }
            }

            if (live.Count > 0) {
                Rearm();
                return;
            }

            ILHook? stale = hook;
            hook = null;
            ordered = Array.Empty<Injection>();
            owners = Array.Empty<string>();
            composeFailed = false;
            stale?.Dispose();
        }
    }

    private void Rearm() {
        ordered = InjectionOrderer.OrderForComposition(live);
        owners = BuildOwners(ordered);

        if (hook is null) {
            ILHookConfig config = default;
            config.ID = HookId;
            config.Before = new[] { "*" };
            hook = new ILHook(target, Manipulate, ref config);
            return;
        }

        hook.Undo();
        hook.Apply();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "MonoMod leaves the target undetoured for every participant when a manipulator throws out of its chain rebuild, so the compose failure is reported and the body left untouched instead.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2221:Exception should not be caught", Justification = "MonoMod leaves the target undetoured for every participant when a manipulator throws out of its chain rebuild, so the compose failure is reported and the body left untouched instead.")]
    private void Manipulate(ILContext il) {
        IReadOnlyList<Injection> snapshot = ordered;
        composeFailed = false;
        if (snapshot.Count == 0) {
            return;
        }

        try {
            ILContextConverter.Compose(target, snapshot, il, HarmonyXBackend.Write);
        } catch (Exception failure) {
            composeFailed = true;
            HarmonyXBackend.Write(
                FailedMarker + " '" + HarmonyXBackend.Describe(target) + "': " + ExceptionReason.Of(failure));
        }
    }

    private sealed class Handle : IBackendHandle {
        private readonly HarmonyXTarget owner;

        private List<long>? owned;

        internal Handle(HarmonyXTarget owner, List<long> owned) {
            this.owner = owner;
            this.owned = owned;
        }

        public MethodBase Original => owner.target;

        public bool IsApplied => Volatile.Read(ref owned) is not null && owner.IsLive;

        public void Dispose() {
            List<long>? mine = Interlocked.Exchange(ref owned, null);
            if (mine is not null) {
                owner.Remove(mine);
            }
        }
    }
}
