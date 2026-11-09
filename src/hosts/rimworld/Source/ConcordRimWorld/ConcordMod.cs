using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace Concord.RimWorld;

public class ConcordMod : Mod {
  public ConcordMod(ModContentPack content) : base(content) {
    Log.Message("[Concord.RimWorld] Concord v" + VersionInfo.Adapter + " loading (runtime v" + VersionInfo.Runtime + ", runtime assembly #" + typeof(Patcher).Assembly.GetHashCode() + ", adapter assembly #" + typeof(ConcordMod).Assembly.GetHashCode() + ").");

    if (ShippedConcordCannotLoad()) {
      Log.Error(
          "[Concord.RimWorld] The shipped Concord runtime depends on System.Reflection.Emit " +
          "facade assemblies that this runtime does not provide. Concord wiring is skipped; " +
          "mods depending on Concord will not be patched. A runtime-compatible Concord build " +
          "is required.");
      return;
    }

    try {
      CheckLoadOrder(content);
      RimWorldAdapter.Wire(content);
      if (RimWorldAdapter.Ready) {
        Log.Message("[Concord.RimWorld] Concord runtime wired.");
      }
    } catch (Exception e) {
      Log.Error("[Concord.RimWorld] Failed to initialize Concord: " + e);
    }
  }

  private static bool ShippedConcordCannotLoad() {
    Assembly concord = AppDomain.CurrentDomain.GetAssemblies()
        .FirstOrDefault(assembly => ModLayout.SimpleName(assembly) == "Concord");

    if (concord == null) {
      return false;
    }

    bool referencesFacade = concord.GetReferencedAssemblies().Any(reference =>
        reference.Name == "System.Reflection.Emit.Lightweight" ||
        reference.Name == "System.Reflection.Emit.ILGeneration");

    if (!referencesFacade) {
      return false;
    }

    return Type.GetType(
        "System.Reflection.Emit.DynamicMethod, System.Reflection.Emit.Lightweight, " +
        "Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a",
        false) == null;
  }

  private static void CheckLoadOrder(ModContentPack content) {
    string selfId = content.PackageId;
    string coreId = ModContentPack.CoreModPackageId;
    List<string> before = [];
    bool coreBefore = false;
    foreach (ModMetaData mod in ModsConfig.ActiveModsInLoadOrder) {
      if (mod == null) {
        continue;
      }

      string id = mod.PackageId;
      if (string.Equals(id, selfId, StringComparison.OrdinalIgnoreCase)) {
        break;
      }

      if (string.Equals(id, coreId, StringComparison.OrdinalIgnoreCase)) {
        coreBefore = true;
      }

      before.Add(id);
    }

    if (coreBefore) {
      Log.Error(
          "[Concord.RimWorld] LOAD ORDER ERROR: core (" + coreId + ") loads before Concord. " +
          "Concord loads the patching runtime and MUST load before core and every other mod. " +
          "Move " + selfId + " to the very top of the mod list. Mods loaded before Concord: " +
          string.Join(", ", before));
      return;
    }

    if (before.Count > 0) {
      Log.Warning(
          "[Concord.RimWorld] LOAD ORDER WARNING: " + before.Count + " mod(s) load before Concord (" +
          string.Join(", ", before) + "). Concord should load first so its patching runtime is " +
          "available to every other mod. Move " + selfId + " to the top of the mod list.");
    }
  }
}
