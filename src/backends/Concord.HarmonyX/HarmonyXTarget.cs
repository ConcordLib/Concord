using System.Reflection;
using System.Threading;
using Concord.Backend;
using Concord.Emit;
using Concord.MonoMod;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Concord.HarmonyX;

internal sealed class HarmonyXTarget {
    internal const string HookId = "Concord";

    internal const string ScopesHookId = "Concord.Scopes";

    private const string FailedMarker = "[Concord.HarmonyX] compose-failed";

    private const string SharedMarker = "[Concord.HarmonyX] shared-scope";

    private static readonly Dictionary<MethodBase, HarmonyXTarget> Targets = new Dictionary<MethodBase, HarmonyXTarget>();

    private static readonly object TargetsGate = new object();

    private readonly MethodBase target;

    private readonly object gate = new object();

    private readonly List<(long Seq, Injection Injection)> live = new List<(long Seq, Injection Injection)>();

    private ILHook? hook;

    private ILHook? scopes;

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

    private static void Drop(ILHook? stale) {
        if (stale is null) {
            return;
        }

        try {
            stale.Undo();
        } finally {
            stale.Free();
        }
    }

    private IBackendHandle AddInternal(IReadOnlyList<Injection> added) {
        lock (gate) {
            bool armed = hook is not null;
            List<long> owned = new List<long>(added.Count);
            foreach (Injection injection in added) {
                long seq = sequence++;
                live.Add((seq, injection));
                owned.Add(seq);
            }

            try {
                Arm();
            } catch {
                live.RemoveAll(entry => owned.Contains(entry.Seq));
                if (armed) {
                    Restore();
                } else {
                    Abandon();
                }

                throw;
            }

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

            TearDown();
        }
    }

    private void Rearm() {
        try {
            Arm();
        } catch {
            Abandon();
            throw;
        }
    }

    private void Arm() {
        ordered = InjectionOrderer.OrderForComposition(live);
        owners = BuildOwners(ordered);

        if (hook is null) {
            ILHookConfig last = default;
            last.ID = ScopesHookId;
            last.After = new[] { "*" };
            last.ManualApply = true;
            scopes = new ILHook(target, BindScopes, ref last);

            ILHookConfig config = default;
            config.ID = HookId;
            config.Before = new[] { "*" };
            config.ManualApply = true;
            hook = new ILHook(target, Manipulate, ref config);
            scopes.Apply();
            hook.Apply();
            return;
        }

        hook.Undo();
        hook.Apply();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The rearm that failed first is the exception the caller receives, so a second refusal from the chain is reported and the earlier injections are dropped.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2221:Exception should not be caught", Justification = "The rearm that failed first is the exception the caller receives, so a second refusal from the chain is reported and the earlier injections are dropped.")]
    private void Restore() {
        try {
            Arm();
        } catch (Exception failure) {
            Report(failure);
            Abandon();
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The rearm that failed first is the exception the caller receives, so a refusal while the hooks leave the chain is reported instead.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2221:Exception should not be caught", Justification = "The rearm that failed first is the exception the caller receives, so a refusal while the hooks leave the chain is reported instead.")]
    private void Abandon() {
        try {
            TearDown();
        } catch (Exception failure) {
            Report(failure);
        }
    }

    private void TearDown() {
        ILHook? stale = hook;
        ILHook? staleScopes = scopes;
        hook = null;
        scopes = null;
        ordered = Array.Empty<Injection>();
        owners = Array.Empty<string>();
        composeFailed = false;
        try {
            Drop(stale);
        } finally {
            Drop(staleScopes);
        }
    }

    private void Report(Exception failure) {
        HarmonyXBackend.Write(FailedMarker + " '" + HarmonyXBackend.Describe(target) + "': " + ExceptionReason.Of(failure));
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "MonoMod leaves the target undetoured for every participant when a manipulator throws out of its chain rebuild, so the compose failure is reported and the body left untouched instead.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2221:Exception should not be caught", Justification = "MonoMod leaves the target undetoured for every participant when a manipulator throws out of its chain rebuild, so the compose failure is reported and the body left untouched instead.")]
    private void Manipulate(ILContext il) {
        IReadOnlyList<Injection> snapshot = ordered;
        composeFailed = false;
        if (snapshot.Count == 0) {
            return;
        }

        ExactScopes.Seed(il.Method.Module);
        ExactScopes.Bind(il.Method.Module);
        try {
            ILContextConverter.Compose(target, snapshot, il, HarmonyXBackend.Write);
        } catch (Exception failure) {
            composeFailed = true;
            Report(failure);
        }
    }

    private void BindScopes(ILContext il) {
        ExactScopes.Bind(il.Method.Module, out int shared);
        if (shared > 0) {
            HarmonyXBackend.Write(
                SharedMarker + " '" + HarmonyXBackend.Describe(target) + "': " + shared +
                " assembly reference(s) each stand for two loaded copies that share a name and a hash, so a call through one resolves into a single copy.");
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
