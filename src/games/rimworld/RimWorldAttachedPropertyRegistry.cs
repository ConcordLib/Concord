using System;
using System.Collections.Generic;
using System.Linq;
using Concord.AttachedData;
using Concord.Orchestration;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Concord.RimWorld;

public sealed class RimWorldAttachedPropertyRegistry : IAttachedPropertyRegistry {
  private readonly List<Entry> entries = [];
  private readonly HashSet<string> keys = [];
  private readonly Dictionary<Type, Entry[]> byType = [];

  public bool IsEmpty => entries.Count == 0;

  public void RegisterAttachedProperty(Type declarationType, Type baseType, string name, Type valueType, IAttachedSlot slot) {
    string key = ModLayout.SimpleName(declarationType.Assembly) + "." + name;
    if (!IsPersisted(baseType)) {
      Log.Warning("[Concord.RimWorld] Attached field '" + declarationType.FullName + "." + name + "' targets " + baseType.Name + ", which Concord does not scribe. It will work, but it will not be saved. Concord hooks Thing, Faction, WorldObject, Hediff, ThingComp, MapComponent, WorldComponent and GameComponent.");
    }

    try {
      Add(baseType, key, valueType, slot);
    } catch (Exception e) {
      Log.Error("[Concord.RimWorld] Attached field '" + declarationType.FullName + "." + name + "' will not be saved: " + e.Message);
    }
  }

  public IReadOnlyList<Entry> ForBaseType(Type type) {
    if (byType.TryGetValue(type, out Entry[] cached)) {
      return cached;
    }

    Entry[] array = [.. entries.Where(entry => entry.BaseType.IsAssignableFrom(type))];
    byType[type] = array;
    return array;
  }

  internal void Add(Type baseType, string key, Type valueType, IAttachedSlot slot) {
    if (IsBclType(baseType)) {
      throw new ArgumentException("Attached properties cannot target BCL types: " + baseType.FullName, nameof(baseType));
    }

    if (!IsSupportedValueType(valueType)) {
      throw new ArgumentException("Attached-property type is not supported for save/load: " + valueType.FullName, nameof(valueType));
    }

    string composite = baseType.FullName + "::" + key;
    if (!keys.Add(composite)) {
      throw new InvalidOperationException("Duplicate attached property key: " + composite);
    }

    string label = "concord." + Label(baseType) + "." + key;
    if (!IsXmlName(label)) {
      throw new ArgumentException("Attached-property name is not usable as a save label: " + label, nameof(key));
    }

    entries.Add(new Entry(baseType, key, valueType, slot, label, Scribers.For(valueType, slot, label)));
    byType.Clear();
  }

  private static string Label(Type baseType) {
    return baseType.FullName.Replace('+', '.');
  }

  private static bool IsXmlName(string label) {
    for (int i = 0; i < label.Length; i++) {
      char c = label[i];
      if (!char.IsLetterOrDigit(c) && c != '.' && c != '_' && c != '-') {
        return false;
      }
    }

    return label.Length > 0 && (char.IsLetter(label[0]) || label[0] == '_');
  }

  private static bool IsSupportedValueType(Type valueType) {
    return valueType.IsEnum || ParseHelper.HandlesType(valueType);
  }

  private static bool IsBclType(Type type) {
    string ns = type.Namespace;
    if (ns == null) {
      return false;
    }

    return ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal);
  }

  private static readonly Type[] scribedRoots = [
        typeof(Thing), typeof(Faction), typeof(WorldObject), typeof(Hediff),
        typeof(ThingComp), typeof(MapComponent), typeof(WorldComponent), typeof(GameComponent),
    ];

  private static bool IsPersisted(Type baseType) {
    return Array.Exists(scribedRoots, root => root.IsAssignableFrom(baseType));
  }

  public sealed record Entry(Type BaseType, string Key, Type ValueType, IAttachedSlot Slot, string ScribeLabel, Action<object> Scribe);
}
