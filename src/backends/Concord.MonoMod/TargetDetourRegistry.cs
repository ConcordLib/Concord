using System.Reflection;
using Concord.Backend;
using Concord.Emit;
using MonoMod.Core;
using MonoMod.Core.Platforms;

namespace Concord.MonoMod;

internal sealed class TargetDetourRegistry {
    private static readonly Dictionary<MethodBase, TargetDetourRegistry> Registries = new Dictionary<MethodBase, TargetDetourRegistry>();
    private static readonly object RegistriesGate = new object();
    private static readonly HashSet<MethodBase> ForeignOwned = [];

    // never released: mono frees a collected wrapper's code while a thread may still be inside it.
    private static readonly List<MethodInfo> RootedWrappers = [];
    private static readonly object RootedGate = new object();

    private static readonly List<ICoreDetour> DetoursRootedSoTheirFinalizerNeverUndoesThem = [];

    private static readonly Dictionary<IntPtr, MethodBase> Wrappers = new Dictionary<IntPtr, MethodBase>();
    private static readonly object WrappersGate = new object();

    private static int reflectionOnlyAtLastCompose;
    private static int namesLoadedTwiceAtLastCompose;

    private readonly MethodBase target;
    private readonly object gate = new object();
    private readonly List<(long Seq, Injection Injection)> live = [];
    private readonly List<IntPtr> wrapperKeys = [];
    private ICoreDetour? detour;
    private long sequence;
    private bool superseded;
    private IReadOnlyList<string> owners = [];

    private TargetDetourRegistry(MethodBase target) {
        this.target = target;
    }

    internal static int RootedWrapperCount {
        get {
            lock (RootedGate) {
                return RootedWrappers.Count;
            }
        }
    }

    internal static int PatchedTargetCount {
        get {
            List<TargetDetourRegistry> all;
            lock (RegistriesGate) {
                all = new List<TargetDetourRegistry>(Registries.Values);
            }

            int patched = 0;
            foreach (TargetDetourRegistry registry in all) {
                if (registry.IsApplied) {
                    patched++;
                }
            }

            return patched;
        }
    }

    private bool IsApplied {
        get {
            lock (gate) {
                return detour is { IsApplied: true };
            }
        }
    }

    internal static IBackendHandle Add(MethodBase target, IReadOnlyList<Injection> added) {
        MethodBase key = MethodIdentity.SharedBodyKey(target);
        added = WrapperComposer.TagRequestedInstantiation(target, added);

        TargetDetourRegistry registry;
        lock (RegistriesGate) {
            if (!Registries.TryGetValue(key, out registry!)) {
                registry = new TargetDetourRegistry(key) { superseded = ForeignOwned.Contains(key) };
                Registries[key] = registry;
            }
        }

        return registry.AddInternal(added);
    }

    internal static IReadOnlyList<string> OwnersFor(MethodBase target) {
        TargetDetourRegistry? registry = Find(MethodIdentity.SharedBodyKey(target));
        if (registry == null) {
            return [];
        }

        lock (registry.gate) {
            return registry.owners;
        }
    }

    internal static bool OwnsBody(MethodBase target) {
        MethodBase key = MethodIdentity.SharedBodyKey(target);
        TargetDetourRegistry? registry = Find(key);
        if (registry == null) {
            return false;
        }

        lock (RegistriesGate) {
            if (ForeignOwned.Contains(registry.target)) {
                return false;
            }
        }

        return registry.IsApplied;
    }

    internal static bool RecomposeLive(MethodBase target) {
        MethodBase key = MethodIdentity.SharedBodyKey(target);
        TargetDetourRegistry? registry = Find(key);
        if (registry == null) {
            MethodBase? resolved = ResolveOriginal(target);
            registry = resolved == null ? null : Find(MethodIdentity.SharedBodyKey(resolved));
            if (registry == null) {
                return false;
            }
        }

        lock (RegistriesGate) {
            if (ForeignOwned.Contains(registry.target)) {
                MonoModHost.Write(
                    $"[Concord] recompose refused for '{registry.target.DeclaringType?.FullName}.{registry.target.Name}': a foreign patch host wrote the body.");
                return false;
            }
        }

        try {
            return registry.RecomposeFromLive();
        } catch (Exception failure) {
            MonoModHost.Write(
                $"[Concord] recompose failed for '{registry.target.DeclaringType?.FullName}.{registry.target.Name}': {failure.Message}");
            return false;
        }
    }

    internal static void MarkForeignOwned(MethodBase key) {
        lock (RegistriesGate) {
            ForeignOwned.Add(key);
        }
    }

    internal static void Supersede(MethodBase target) {
        MethodBase key = MethodIdentity.SharedBodyKey(target);
        MarkForeignOwned(key);
        Find(key)?.SupersedeInternal();
    }

    internal static bool IsSuperseded(MethodBase target) {
        TargetDetourRegistry? registry = Find(MethodIdentity.SharedBodyKey(target));
        if (registry == null) {
            return false;
        }

        lock (registry.gate) {
            return registry.superseded;
        }
    }

    internal static MethodBase? ResolveOriginal(MethodBase method) {
        if (method is null) {
            throw new ArgumentNullException(nameof(method));
        }

        if (!TryKeysOf(method, out IntPtr handle, out IntPtr entryPoint)) {
            return null;
        }

        lock (WrappersGate) {
            if (Wrappers.TryGetValue(handle, out MethodBase? byHandle)) {
                return byHandle;
            }

            return Wrappers.TryGetValue(entryPoint, out MethodBase? byEntryPoint) ? byEntryPoint : null;
        }
    }

    private static void Remember(MethodInfo wrapper, MethodBase target, List<IntPtr> keys) {
        if (!TryKeysOf(wrapper, out IntPtr handle, out IntPtr entryPoint)) {
            return;
        }

        lock (WrappersGate) {
            Wrappers[handle] = target;
            Wrappers[entryPoint] = target;
        }

        keys.Add(handle);
        keys.Add(entryPoint);
    }

    private static void Forget(List<IntPtr> keys) {
        if (keys.Count == 0) {
            return;
        }

        lock (WrappersGate) {
            foreach (IntPtr key in keys) {
                Wrappers.Remove(key);
            }
        }

        keys.Clear();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Attribution is diagnostic only: any runtime that will not hand over a handle degrades to an unresolved frame rather than throwing at a caller who is already reporting an error.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2221:Exception should not be caught", Justification = "Attribution is diagnostic only: any runtime that will not hand over a handle degrades to an unresolved frame rather than throwing at a caller who is already reporting an error.")]
    private static bool TryKeysOf(MethodBase method, out IntPtr handle, out IntPtr entryPoint) {
        try {
            PlatformTriple triple = PlatformTriple.Current;
            RuntimeMethodHandle resolved = triple.Runtime.GetMethodHandle(triple.GetIdentifiable(method));
            handle = resolved.Value;
            entryPoint = resolved.GetFunctionPointer();
            return handle != IntPtr.Zero || entryPoint != IntPtr.Zero;
        } catch (Exception) {
            handle = IntPtr.Zero;
            entryPoint = IntPtr.Zero;
            return false;
        }
    }

    private static void Root(MethodInfo wrapper) {
        lock (RootedGate) {
            RootedWrappers.Add(wrapper);
        }
    }

    private static void ForgetResolvesIntoReloadedAssemblies() {
        int reflectionOnly = 0;
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        HashSet<string> twice = new HashSet<string>(StringComparer.Ordinal);
        foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies()) {
            if (loaded.ReflectionOnly) {
                reflectionOnly++;
            }

            if (!seen.Add(loaded.GetName().Name)) {
                twice.Add(loaded.GetName().Name);
            }
        }

        if (reflectionOnly <= reflectionOnlyAtLastCompose && twice.Count <= namesLoadedTwiceAtLastCompose) {
            return;
        }

        reflectionOnlyAtLastCompose = reflectionOnly;
        namesLoadedTwiceAtLastCompose = twice.Count;
        ResolveCache.ForgetReflectionOnly();
    }

    private static TargetDetourRegistry? Find(MethodBase target) {
        lock (RegistriesGate) {
            return Registries.TryGetValue(target, out TargetDetourRegistry? registry) ? registry : null;
        }
    }

    // Composition order runs backwards: the body composed last wraps the others, so it runs first.
    private static List<string> BuildOwners(IReadOnlyList<Injection> ordered) {
        if (ordered.Count == 0) {
            return [];
        }

        List<string> result = new List<string>(ordered.Count);
        for (int i = ordered.Count - 1; i >= 0; i--) {
            string owner = ordered[i].Owner;
            if (!result.Contains(owner)) {
                result.Add(owner);
            }
        }

        return result;
    }

    private static void ReportEviction(MethodBase target, RejectedInjection rejection) {
        if (EvictionReport.Compose(CoexistenceLogMarkers.InjectionEvicted, target, rejection) is string report) {
            MonoModHost.Write(report);
        }
    }

    private void SupersedeInternal() {
        lock (gate) {
            if (superseded) {
                return;
            }

            superseded = true;
            ICoreDetour? held = detour;
            detour = null;
            Forget(wrapperKeys);

            if (held != null) {
                lock (RootedGate) {
                    DetoursRootedSoTheirFinalizerNeverUndoesThem.Add(held);
                }
            }
        }
    }

    private IBackendHandle AddInternal(IReadOnlyList<Injection> added) {
        lock (gate) {
            List<long> owned = new List<long>(added.Count);
            List<(long Seq, Injection Injection)> tentative = new List<(long Seq, Injection Injection)>(live.Count + added.Count);
            tentative.AddRange(live);
            bool debug = false;
            long nextSequence = sequence;
            foreach (Injection injection in added) {
                long seq = nextSequence++;
                tentative.Add((seq, injection));
                owned.Add(seq);
                debug |= injection.Debug;
            }

            Injection[] ordered = InjectionOrderer.OrderForComposition(tentative);
            List<(long Seq, Injection Injection)> previous = new List<(long Seq, Injection Injection)>(live);
            long previousSequence = sequence;
            live.Clear();
            live.AddRange(tentative);
            sequence = nextSequence;
            RecomposeOrRestore(ordered, debug, previous, previousSequence);
            return new RegistryHandle(this, owned);
        }
    }

    private bool RecomposeFromLive() {
        lock (gate) {
            if (live.Count == 0) {
                return false;
            }

            bool debug = false;
            foreach ((long Seq, Injection Injection) entry in live) {
                debug |= entry.Injection.Debug;
            }

            try {
                Recompose(InjectionOrderer.OrderForComposition(live), debug);
            } catch {
                ICoreDetour? stale = detour;
                detour = null;
                Forget(wrapperKeys);
                if (stale is { IsApplied: true }) {
                    stale.Undo();
                }

                stale?.Dispose();
                throw;
            }

            return true;
        }
    }

    private void Remove(List<long> owned) {
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

            RecomposeOrRestore(InjectionOrderer.OrderForComposition(live), false, previous, null);
        }
    }

    private void RecomposeOrRestore(
        IReadOnlyList<Injection> ordered,
        bool debug,
        List<(long Seq, Injection Injection)> previous,
        long? previousSequence) {
        try {
            Recompose(ordered, debug);
        } catch (Exception primaryFailure) {
            live.Clear();
            live.AddRange(previous);
            if (previousSequence.HasValue) {
                sequence = previousSequence.Value;
            }

            bool restoredDebug = false;
            foreach ((long Seq, Injection Injection) entry in live) {
                restoredDebug |= entry.Injection.Debug;
            }

            try {
                Recompose(InjectionOrderer.OrderForComposition(live), restoredDebug);
            } catch (Exception rollbackFailure) {
                throw new AggregateException(primaryFailure, rollbackFailure);
            }

            throw;
        }
    }

    private void Recompose(IReadOnlyList<Injection> ordered, bool debug) {
        if (superseded) {
            if (ordered.Count > 0) {
                WrapperComposer.ValidateComposition(target, ordered);
            }

            owners = BuildOwners(ordered);
            return;
        }

        ComposeResult? composed = null;
        if (ordered.Count > 0) {
            ForgetResolvesIntoReloadedAssemblies();

            if (debug) {
                PatchDebugLog.Append(target, WrapperComposer.ComposeDump(target, ordered));
            }

            composed = WrapperComposer.Compose(target, ordered);
            foreach (RejectedInjection rejection in composed.Rejected) {
                ReportEviction(target, rejection);
            }
        }

        using (WrapperPrecompile.Enter()) {
            ICoreDetour? old = detour;
            detour = null;
            Forget(wrapperKeys);
            if (old is { IsApplied: true }) {
                old.Undo();
            }

            old?.Dispose();

            if (composed is not null) {
                try {
                    WrapperPrecompile.Compile(composed.Wrapper);
                    detour = MonoModHost.Factory.CreateDetour(target, composed.Wrapper);
                    Root(composed.Wrapper);
                } catch (InvalidProgramException rejected) {
                    throw new ConcordEmitException(
                        "CONC144",
                        $"The runtime rejected the composed body for '{target.DeclaringType?.FullName}.{target.Name}': {rejected.Message}. " +
                        "Read the assemblies section below. Two rows under one name means two copies of that assembly are loaded.\n" +
                        WrapperComposer.ComposeDump(target, ordered));
                }

                Remember(composed.Wrapper, target, wrapperKeys);
            }
        }

        owners = BuildOwners(ordered);
    }

    private sealed class RegistryHandle : IBackendHandle {
        private readonly TargetDetourRegistry owner;
        private readonly List<long> owned;
        private bool disposed;

        public RegistryHandle(TargetDetourRegistry owner, List<long> owned) {
            this.owner = owner;
            this.owned = owned;
        }

        public MethodBase Original => owner.target;

        public bool IsApplied => !disposed && owner.IsApplied;

        public void Dispose() {
            if (disposed) {
                return;
            }

            owner.Remove(owned);
            disposed = true;
        }
    }
}
