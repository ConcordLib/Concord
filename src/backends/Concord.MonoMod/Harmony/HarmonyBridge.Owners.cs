using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Concord.MonoMod.Harmony;

/// <content>Upstream Harmony flavour of the foreign owner scan, including inner patches.</content>
public sealed partial class HarmonyBridge {
    private static partial IReadOnlyList<string> CollectForeignOwners(MethodBase target) {
        Patches? patchInfo = PatchProcessor.GetPatchInfo(target);
        if (patchInfo == null) {
            return Array.Empty<string>();
        }

        HashSet<string> owners = new HashSet<string>();
        CollectForeignOwners(patchInfo.Prefixes, owners);
        CollectForeignOwners(patchInfo.Postfixes, owners);
        CollectForeignOwners(patchInfo.Transpilers, owners);
        CollectForeignOwners(patchInfo.Finalizers, owners);
        CollectForeignOwners(InnerCategory(patchInfo, "InnerPrefixes"), owners);
        CollectForeignOwners(InnerCategory(patchInfo, "InnerPostfixes"), owners);

        return new List<string>(owners);
    }

    private static IReadOnlyList<Patch> InnerCategory(Patches patchInfo, string name) {
        PropertyInfo? property = typeof(Patches).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        return property?.GetValue(patchInfo) as IReadOnlyList<Patch> ?? Array.Empty<Patch>();
    }
}
