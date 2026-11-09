using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Backend;
using Concord.Emit;
using HarmonyLib;

namespace Concord.MonoMod.Harmony;

internal static class UpdateWrapperHook {
    private static readonly string[] PatchCategories = {
        "prefixes", "postfixes", "transpilers", "finalizers", "innerprefixes", "innerpostfixes", "innerfinalizers",
    };

    private static readonly MethodInfo? NotifyMethod =
        typeof(UpdateWrapperHook).GetMethod(nameof(Notify), BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly MethodInfo? NotifyForeignCopyMethod =
        typeof(UpdateWrapperHook).GetMethod(nameof(NotifyForeignCopy), BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly MethodInfo? NotifyCommittedMethod =
        typeof(UpdateWrapperHook).GetMethod(nameof(NotifyCommitted), BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly Dictionary<Assembly, Copy> Copies = new Dictionary<Assembly, Copy>();

    private static readonly List<Copy> Armed = new List<Copy>();

    private static readonly Dictionary<Assembly, MethodInfo?> PublicPatchIndex = new Dictionary<Assembly, MethodInfo?>();

    [ThreadStatic]
    private static int suppressDepth;

    private static bool watchingForCopies;

    private static IForeignPatchObserver? observer;

    private static Action<string>? logger;

    private static MonoModBackend? hookBackend;

    internal static bool ThrowOnPatchInfoReadForTests { get; set; }

    internal static string? DeniedMemberForTests { get; set; }

    internal static IReadOnlyDictionary<Assembly, MethodInfo?> PublicPatchIndexForTests() {
        lock (PublicPatchIndex) {
            return new Dictionary<Assembly, MethodInfo?>(PublicPatchIndex);
        }
    }

    internal static bool TryInstall(IForeignPatchObserver target, MonoModBackend rawBackend, Action<string> log) {
        observer = target;
        logger = log;
        hookBackend = rawBackend;

        if (NotifyMethod == null || NotifyForeignCopyMethod == null || NotifyCommittedMethod == null) {
            log(CoexistenceLogMarkers.HookUnavailable + " the notify injection method was not found");
            return false;
        }

        WatchForLaterCopies();

        int armed = InstallEveryLoadedCopy(rawBackend, log);

        if (armed == 0) {
            log(CoexistenceLogMarkers.HookUnavailable +
                " every loaded Harmony copy was refused or failed to arm, each for the reason logged above it. A foreign patch arriving later stays unseen");
            return false;
        }

        return true;
    }

    internal static IDisposable EnterAllArmed() {
        Assembly compileTime = typeof(HarmonyLib.Harmony).Assembly;
        LockSet entered = new LockSet();
        bool complete = false;

        try {
            entered.Add(HarmonyLockScope.For(compileTime));

            foreach (Copy copy in Snapshot()) {
                if (copy.Owner != compileTime) {
                    entered.Add(HarmonyLockScope.For(copy.Owner));
                }
            }

            complete = true;
        } finally {
            if (!complete) {
                entered.Dispose();
            }
        }

        return entered;
    }

    internal static IDisposable Suppress() {
        suppressDepth++;
        return new SuppressScope();
    }

    internal static void ResetForTests() {
        List<Copy> armed;
        lock (Copies) {
            armed = new List<Copy>(Armed);
            Copies.Clear();
            Armed.Clear();
        }

        foreach (Copy copy in armed) {
            copy.Notifier.Dispose();
        }

        MethodCopierHook.ResetForTests();
    }

    internal static void DetachObserver() {
        observer = null;
        hookBackend = null;
    }

    internal static string HarmonyVersionText() {
        return VersionTextOf(typeof(HarmonyLib.Harmony).Assembly);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static bool BindingIsReflectionOnly() {
        return typeof(HarmonyLib.Harmony).Assembly.ReflectionOnly;
    }

    internal static bool IsHookedOn(Assembly copy) {
        if (copy == null) {
            return false;
        }

        lock (Copies) {
            return Copies.ContainsKey(copy);
        }
    }

    internal static void StopWatchingForCopies() {
        lock (Copies) {
            if (!watchingForCopies) {
                return;
            }

            watchingForCopies = false;
        }

        AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
    }

    internal static bool HasPatch(MethodBase target) {
        List<Copy> copies = Snapshot();

        foreach (Copy copy in copies) {
            object? info = ReadPatchInfo(copy, target);
            if (info != null) {
                if (CarriesAnyOwner(info)) {
                    return true;
                }

                continue;
            }

            if (!copy.StateReadable && HasPublicOwner(copy.Owner, target)) {
                return true;
            }
        }

        if (HasPublicOwner(typeof(HarmonyLib.Harmony).Assembly, target)) {
            return true;
        }

        foreach (Assembly indexed in PublicIndexedCopies()) {
            if (indexed == typeof(HarmonyLib.Harmony).Assembly || IsHookedOn(indexed)) {
                continue;
            }

            if (HasPublicOwner(indexed, target)) {
                return true;
            }
        }

        return false;
    }

    internal static bool RequestRebuild(MethodBase target, out string refusals) {
        List<Copy> copies = Snapshot();
        List<string> refused = new List<string>(copies.Count);

        foreach (Copy copy in copies) {
            if (copy.GetPatchInfo is not { } read || !copy.StateReadable) {
                refused.Add(Describe(copy.Owner) + " cannot read its shared patch state");
                continue;
            }

            object? info;
            try {
                info = read.Invoke(null, new object?[] { target });
            } catch (Exception ex) {
                refused.Add(Describe(copy.Owner) + " failed to read its patch record: " + ExceptionReason.Of(ex));
                continue;
            }

            if (info == null) {
                refused.Add(Describe(copy.Owner) + " holds no patch record for this method");
                continue;
            }

            try {
                using (HarmonyLockScope.For(copy.Owner))
                using (Suppress()) {
                    copy.UpdateWrapper.Invoke(null, new object?[] { target, info });
                }
            } catch (Exception ex) {
                refused.Add(Describe(copy.Owner) + " threw while rebuilding: " + ExceptionReason.Of(ex));
                continue;
            }

            refusals = string.Empty;
            return true;
        }

        refusals = refused.Count == 0 ? "no Harmony copy is hooked" : string.Join("; ", refused.ToArray());
        return false;
    }

    internal static bool TryInstallFor(Assembly copy, MonoModBackend rawBackend, Action<string> log) {
        if (BindingIsReflectionOnly()) {
            return false;
        }

        lock (Copies) {
            if (Copies.ContainsKey(copy)) {
                return true;
            }
        }

        try {
            if (copy.GetType("HarmonyLib.Harmony") == null) {
                return false;
            }

            if (copy.ReflectionOnly) {
                log(CoexistenceLogMarkers.HookSkipped + " " + Describe(copy) +
                    " is loaded reflection-only, so nothing can run through it and it is not armed");
                return false;
            }
        } catch (Exception ex) {
            log(CoexistenceLogMarkers.HookUnavailable + " " + Describe(copy) + ": " + ExceptionReason.Of(ex));
            return false;
        }

        using (LockOf(copy, log)) {
            return ArmCopy(copy, rawBackend, log);
        }
    }

    private static bool ArmCopy(Assembly copy, MonoModBackend rawBackend, Action<string> log) {
        MethodInfo? updateWrapper;
        MethodInfo? getPatchInfo;
        try {
            updateWrapper = ResolveUpdateWrapper(copy);
            getPatchInfo = ResolveGetPatchInfo(copy);
        } catch (Exception ex) {
            log(CoexistenceLogMarkers.HookUnavailable + " " + Describe(copy) + ": " + ExceptionReason.Of(ex));
            return false;
        }

        if (updateWrapper == null) {
            log(CoexistenceLogMarkers.HookUnavailable + " " + Describe(copy) +
                " carries no HarmonyLib.PatchFunctions.UpdateWrapper, so patches made through that copy stay invisible");
            return false;
        }

        if (getPatchInfo == null) {
            log(CoexistenceLogMarkers.HookDegraded + " " + Describe(copy) +
                " carries no HarmonyLib.HarmonySharedState.GetPatchInfo, so its shared patch state cannot be read and a rebuild through this copy cannot be asked for. Its patches are still seen through Harmony's public patch index");
        }

        bool stateReadable = getPatchInfo != null && TryInitialiseSharedState(copy, getPatchInfo, log);

        if (!MethodCopierHook.TryInstallFor(copy, rawBackend, log, out string? copierMissing)) {
            log(CoexistenceLogMarkers.HookUnavailable + " " + Describe(copy) + ": " + copierMissing +
                ", so Concord's composed body cannot reach that copy's wrapper build and the copy is refused whole");
            return false;
        }

        IBackendHandle handle;
        try {
            MethodInfo notifyMethod = (copy == typeof(HarmonyLib.Harmony).Assembly ? NotifyMethod : NotifyForeignCopyMethod)!;
            Injection notify = new Injection(notifyMethod, new InjectAt.Head(), "concord.hook", 0);
            Injection returned = new Injection(NotifyCommittedMethod!, new InjectAt.Return(), "concord.hook", 0);
            handle = rawBackend.ApplyComposed(updateWrapper, new[] { notify, returned });
        } catch (Exception ex) {
            MethodCopierHook.RemoveFor(copy);
            log(CoexistenceLogMarkers.HookUnavailable + " " + Describe(copy) + ": " + ExceptionReason.Of(ex));
            return false;
        }

        lock (Copies) {
            if (Copies.ContainsKey(copy)) {
                handle.Dispose();
                return true;
            }

            Copy armed = new Copy(copy, updateWrapper, getPatchInfo, handle, stateReadable);
            Copies[copy] = armed;
            Armed.Add(armed);
        }

        log(CoexistenceLogMarkers.HookInstalled + " " + Describe(copy));
        return true;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Harmony's public patch index is read for an ownership check only, so a copy whose public API throws reports no owner rather than failing the caller.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2221:Exception should not be caught", Justification = "Harmony's public patch index is read for an ownership check only, so a copy whose public API throws reports no owner rather than failing the caller.")]
    private static bool HasPublicOwner(Assembly copy, MethodBase target) {
        try {
            object? patches = PublicPatchIndexOf(copy)?.Invoke(null, new object?[] { target });
            if (patches == null) {
                return false;
            }

            PropertyInfo? owners = patches.GetType().GetProperty("Owners", BindingFlags.Public | BindingFlags.Instance);
            return owners?.GetValue(patches) is System.Collections.ICollection collection && collection.Count > 0;
        } catch (Exception) {
            return false;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A copy whose type load throws carries no readable public patch index, so it is recorded as having none rather than failing the caller.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2221:Exception should not be caught", Justification = "A copy whose type load throws carries no readable public patch index, so it is recorded as having none rather than failing the caller.")]
    private static MethodInfo? ResolvePublicPatchIndex(Assembly copy) {
        try {
            return copy.GetType("HarmonyLib.PatchProcessor")?.GetMethod(
                "GetPatchInfo", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(MethodBase) }, null);
        } catch (Exception) {
            return null;
        }
    }

    private static MethodInfo? PublicPatchIndexOf(Assembly copy) {
        lock (PublicPatchIndex) {
            if (!PublicPatchIndex.TryGetValue(copy, out MethodInfo? found)) {
                found = ResolvePublicPatchIndex(copy);
                PublicPatchIndex[copy] = found;
            }

            return found;
        }
    }

    private static void NotePublicPatchIndex(Assembly loaded) {
        if (loaded.ReflectionOnly) {
            return;
        }

        PublicPatchIndexOf(loaded);
    }

    private static List<Assembly> PublicIndexedCopies() {
        List<Assembly> found = new List<Assembly>();

        lock (PublicPatchIndex) {
            foreach (KeyValuePair<Assembly, MethodInfo?> entry in PublicPatchIndex) {
                if (entry.Value != null) {
                    found.Add(entry.Key);
                }
            }
        }

        return found;
    }

    private static IDisposable? LockOf(Assembly copy, Action<string> log) {
        IDisposable? scope = HarmonyLockScope.For(copy);
        if (scope == null) {
            log(CoexistenceLogMarkers.HookDegraded + " " + Describe(copy) +
                " carries no HarmonyLib.PatchProcessor.locker, so Concord's wrapper build through that copy is not serialized against that copy's own patching");
        }

        return scope;
    }

    private static List<Copy> Snapshot() {
        lock (Copies) {
            return new List<Copy>(Armed);
        }
    }

    private static object? ReadPatchInfo(Copy copy, MethodBase target) {
        if (copy.GetPatchInfo is not { } read || !copy.StateReadable) {
            return null;
        }

        try {
            if (ThrowOnPatchInfoReadForTests) {
                throw new InvalidOperationException("the patch record read is denied for tests");
            }

            return read.Invoke(null, new object?[] { target });
        } catch (Exception ex) {
            if (!copy.ReadFailureLogged) {
                copy.ReadFailureLogged = true;
                logger?.Invoke(CoexistenceLogMarkers.HookDegraded + " " + Describe(copy.Owner) +
                    " failed to read its patch record, so it counts as holding no patch: " + ExceptionReason.Of(ex));
            }

            return null;
        }
    }

    private static bool CarriesAnyOwner(object patchInfo) {
        Type type = patchInfo.GetType();

        foreach (string category in PatchCategories) {
            if (type.GetField(category, BindingFlags.Public | BindingFlags.Instance)?.GetValue(patchInfo) is Array entries && entries.Length > 0) {
                return true;
            }
        }

        return false;
    }

    private static string VersionTextOf(Assembly copy) {
        const string marker = "Version=";
        string? full = copy.FullName;
        if (full == null) {
            return "unknown";
        }

        int start = full.IndexOf(marker, StringComparison.Ordinal);

        if (start < 0) {
            return "unknown";
        }

        start += marker.Length;
        int end = full.IndexOf(',', start);
        return end < 0 ? full.Substring(start) : full.Substring(start, end - start);
    }

    private static int InstallEveryLoadedCopy(MonoModBackend rawBackend, Action<string> log) {
        int armed = 0;
        foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies()) {
            NotePublicPatchIndex(loaded);

            if (TryInstallFor(loaded, rawBackend, log)) {
                armed++;
            }
        }

        return armed;
    }

    private static void WatchForLaterCopies() {
        lock (Copies) {
            if (watchingForCopies) {
                return;
            }

            watchingForCopies = true;
        }

        AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
    }

    private static void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args) {
        NotePublicPatchIndex(args.LoadedAssembly);

        MonoModBackend? backend = hookBackend;
        Action<string>? log = logger;

        if (backend == null || log == null) {
            return;
        }

        try {
            TryInstallFor(args.LoadedAssembly, backend, log);
        } catch (Exception ex) {
            log(CoexistenceLogMarkers.HookUnavailable + " arming a late Harmony copy failed: " + ExceptionReason.Of(ex));
        }
    }

    private static bool TryInitialiseSharedState(Assembly copy, MethodInfo getPatchInfo, Action<string> log) {
        try {
            MethodInfo probe = typeof(UpdateWrapperHook).GetMethod(nameof(NeverPatched), BindingFlags.NonPublic | BindingFlags.Static)!;
            getPatchInfo.Invoke(null, new object?[] { probe });
            return true;
        } catch (Exception ex) {
            log(CoexistenceLogMarkers.HookDegraded + " " + Describe(copy) +
                " could not read its shared patch state, so its patches are seen through Harmony's public patch index only: " + ExceptionReason.Of(ex));
            return false;
        }
    }

    private static void NeverPatched() {
    }

    private static MethodInfo? ResolveUpdateWrapper(Assembly copy) {
        Type? patchFunctions = copy.GetType("HarmonyLib.PatchFunctions");
        return patchFunctions?.GetMethod("UpdateWrapper", BindingFlags.NonPublic | BindingFlags.Static);
    }

    private static MethodInfo? ResolveGetPatchInfo(Assembly copy) {
        if (string.Equals(DeniedMemberForTests, "HarmonySharedState.GetPatchInfo", StringComparison.Ordinal)) {
            return null;
        }

        Type? sharedState = copy.GetType("HarmonyLib.HarmonySharedState");
        return sharedState?.GetMethod("GetPatchInfo", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(MethodBase) }, null);
    }

    private static void Notify(MethodBase? original, PatchInfo patchInfo) {
        if (suppressDepth > 0 || original == null) {
            return;
        }

        IForeignPatchObserver? current = observer;
        current?.OnForeignPatchPending(original, patchInfo);
    }

    private static void NotifyForeignCopy(MethodBase? original) {
        if (suppressDepth > 0 || original == null) {
            return;
        }

        IForeignPatchObserver? current = observer;
        current?.OnForeignPatchPending(original, null!);
    }

    private static void NotifyCommitted(MethodBase? original) {
        if (suppressDepth > 0 || original == null) {
            return;
        }

        (observer as RoutingBackend)?.OnForeignPatchCommitted(original);
    }

    private static string Describe(Assembly copy) {
        return VersionTextOf(copy) + " #" + copy.GetHashCode();
    }

    private sealed class Copy {
        internal Copy(
            Assembly owner,
            MethodInfo updateWrapper,
            MethodInfo? getPatchInfo,
            IBackendHandle notifier,
            bool stateReadable) {
            Owner = owner;
            UpdateWrapper = updateWrapper;
            GetPatchInfo = getPatchInfo;
            Notifier = notifier;
            StateReadable = stateReadable;
        }

        internal Assembly Owner { get; }

        internal MethodInfo UpdateWrapper { get; }

        internal MethodInfo? GetPatchInfo { get; }

        internal IBackendHandle Notifier { get; }

        internal bool StateReadable { get; }

        internal bool ReadFailureLogged { get; set; }
    }

    private sealed class LockSet : IDisposable {
        private readonly List<IDisposable> entered = new List<IDisposable>();

        private bool disposed;

        public void Dispose() {
            if (disposed) {
                return;
            }

            disposed = true;

            for (int i = entered.Count - 1; i >= 0; i--) {
                entered[i].Dispose();
            }
        }

        internal void Add(IDisposable? scope) {
            if (scope != null) {
                entered.Add(scope);
            }
        }
    }

    private sealed class SuppressScope : IDisposable {
        private bool disposed;

        public void Dispose() {
            if (disposed) {
                return;
            }

            disposed = true;
            suppressDepth--;
        }
    }
}
