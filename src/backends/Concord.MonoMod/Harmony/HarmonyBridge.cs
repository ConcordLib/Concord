using System;
using System.Collections.Generic;
using System.Reflection;
using Concord.Backend;
using Concord.Emit;
using HarmonyLib;

namespace Concord.MonoMod.Harmony;

/// <summary>
///     Every loaded copy of Harmony, as one foreign patch host.
/// </summary>
public sealed partial class HarmonyBridge : IForeignPatchHost {
    private readonly Action<string> log;
    private bool foreignOwnersFailureLogged;

    /// <summary>
    ///     Initializes a new instance of the <see cref="HarmonyBridge" /> class.
    /// </summary>
    /// <param name="log">Writes coexistence messages to the host game's log.</param>
    public HarmonyBridge(Action<string> log) {
        this.log = log;
        TranspilerParticipant.Log = log;
    }

    /// <inheritdoc />
    public IDisposable? EnterHostLock() {
        return HarmonyLockScope.Available ? UpdateWrapperHook.EnterAllArmed() : null;
    }

    /// <inheritdoc />
    public bool TryInstallNotifier(IForeignPatchObserver observer, MonoModBackend rawBackend) {
        return UpdateWrapperHook.TryInstall(observer, rawBackend, log);
    }

    /// <inheritdoc />
    public bool HasPatch(MethodBase target) {
        return UpdateWrapperHook.HasPatch(ResolveSharedBodyOwnerOrSelf(MethodIdentity.Normalize(target)));
    }

    /// <inheritdoc />
    public bool TryRebuild(MethodBase target, out string refusals) {
        return UpdateWrapperHook.RequestRebuild(ResolveSharedBodyOwnerOrSelf(MethodIdentity.Normalize(target)), out refusals);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ForeignOwners(MethodBase target) {
        target = ResolveSharedBodyOwnerOrSelf(MethodIdentity.Normalize(target));

        try {
            if (HarmonyLockScope.Available) {
                using (UpdateWrapperHook.EnterAllArmed()) {
                    return CollectForeignOwners(target);
                }
            }

            return CollectForeignOwners(target);
        } catch (Exception ex) {
            if (!foreignOwnersFailureLogged) {
                foreignOwnersFailureLogged = true;
                log($"ForeignOwners read failed for {target}: {ex.Message}");
            }

            return Array.Empty<string>();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ConcordOwners(MethodBase target) {
        return TranspilerParticipant.Registry.OwnersFor(target);
    }

    private static MethodBase ResolveSharedBodyOwnerOrSelf(MethodBase target) {
        try {
            return ResolveSharedBodyOwner(target);
        } catch (Exception) {
            return target;
        }
    }

    private static MethodBase ResolveSharedBodyOwner(MethodBase target) {
        if (!WrapperComposer.SharesGenericBody(target)) {
            return target;
        }

        Type definition = target.DeclaringType!.GetGenericTypeDefinition();
        MethodBase key = MethodIdentity.SharedBodyKey(target);

        foreach (MethodBase patched in HarmonyLib.Harmony.GetAllPatchedMethods()) {
            if (patched.DeclaringType == null || !patched.DeclaringType.IsConstructedGenericType) {
                continue;
            }

            if (patched.DeclaringType.GetGenericTypeDefinition() != definition || patched.Name != target.Name) {
                continue;
            }

            if (MethodIdentity.SharedBodyKey(patched).Equals(key)) {
                return patched;
            }
        }

        return target;
    }

    private static partial IReadOnlyList<string> CollectForeignOwners(MethodBase target);

    private static void CollectForeignOwners(IReadOnlyList<Patch> patches, HashSet<string> owners) {
        for (int i = 0; i < patches.Count; i++) {
            if (patches[i].PatchMethod != TranspilerParticipant.TranspileMethod) {
                owners.Add(patches[i].owner);
            }
        }
    }
}
