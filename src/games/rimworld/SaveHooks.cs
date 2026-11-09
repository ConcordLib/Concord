using System;
using System.Collections.Generic;
using Verse;

namespace Concord.RimWorld;

public static class SaveHooks {
  public static void ScribeAttached(object target) {
    RimWorldAttachedPropertyRegistry registry = RimWorldRuntime.Registry;
    if (registry == null || registry.IsEmpty || target == null) {
      return;
    }

    IReadOnlyList<RimWorldAttachedPropertyRegistry.Entry> entries = registry.ForBaseType(target.GetType());
    for (int i = 0; i < entries.Count; i++) {
      RimWorldAttachedPropertyRegistry.Entry entry = entries[i];
      try {
        entry.Scribe(target);
      } catch (Exception e) {
        Log.Error("[Concord.RimWorld] Failed to scribe attached property '" + entry.Key + "' on " + target.GetType().FullName + ": " + e);
      }
    }
  }
}
