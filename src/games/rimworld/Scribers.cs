using System;
using System.Reflection;
using Concord.AttachedData;
using Verse;

namespace Concord.RimWorld;

internal static class Scribers {
  [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S3011:Reflection should not be used to increase accessibility of classes, methods, or fields", Justification = "Scribers reaches its own private generic builder to close it over a runtime type.")]
  private static readonly MethodInfo buildMethod = typeof(Scribers).GetMethod(nameof(Build), BindingFlags.NonPublic | BindingFlags.Static);

  internal static Action<object> For(Type valueType, IAttachedSlot slot, string label) {
    return (Action<object>)buildMethod.MakeGenericMethod(valueType).Invoke(null, [slot, label]);
  }

  private static Action<object> Build<T>(IAttachedSlot slot, string label) {
    return target => {
      object current = slot.Get(target);
      T value = current is T stored ? stored : default(T);
      Scribe_Values.Look(ref value, label, default(T), false);
      if (Scribe.mode == LoadSaveMode.LoadingVars) {
        slot.Set(target, value);
      }
    };
  }
}
