using System;
using System.Collections.Generic;
using Concord.AttachedData;
using Concord.Orchestration;

namespace Concord.Elin;

public sealed class ElinAttachedPropertyRegistry : IAttachedPropertyRegistry {
    private readonly List<ElinPropertyEntry> entries = [];
    private readonly HashSet<string> keys = [];
    private readonly Dictionary<int, string> ids = [];
    private readonly Dictionary<Type, ElinPropertyEntry[]> byType = [];
    private readonly Action<string> logInfo;
    private readonly Action<string> logWarning;
    private readonly Action<string> logError;

    public ElinAttachedPropertyRegistry(Action<string> logInfo, Action<string> logWarning, Action<string> logError) {
        this.logInfo = logInfo;
        this.logWarning = logWarning;
        this.logError = logError;
    }

    public bool IsEmpty => entries.Count == 0;

    public void RegisterAttachedProperty(Type declarationType, Type baseType, string name, Type valueType, IAttachedSlot slot) {
        BagRoot root = ValueRules.RootOf(baseType);
        if (root == BagRoot.None) {
            logWarning("[Concord.Elin] Attached field '" + declarationType.FullName + "." + name + "' targets " + baseType.Name + ", which Concord does not save. It works in memory only. Concord saves Card (Chara, Thing) and Spatial (Zone).");
        }

        string refusal = ValueRules.Refuse(root, valueType);
        if (refusal != null) {
            logError("[Concord.Elin] Attached field '" + declarationType.FullName + "." + name + "' will not be saved: " + refusal);
            return;
        }

        string label = AttachedKeys.Label(declarationType, name);
        string composite = baseType.FullName + "::" + label;
        if (!keys.Add(composite)) {
            logError("[Concord.Elin] Attached field '" + declarationType.FullName + "." + name + "' will not be saved: duplicate attached property key " + composite + ".");
            return;
        }

        int id = AttachedKeys.Id(label);
        if (ids.TryGetValue(id, out string taken)) {
            keys.Remove(composite);
            if (taken == label) {
                logError("[Concord.Elin] Attached field '" + declarationType.FullName + "." + name + "' will not be saved: attached key " + label + " is already registered. Attached keys are assembly-wide, so two patch classes in one assembly cannot both declare the field " + name + ".");
            } else {
                logError("[Concord.Elin] Attached key " + label + " hashes to " + id + ", already used by " + taken + ". Skipping " + composite + ".");
            }

            return;
        }

        ids[id] = label;
        entries.Add(new ElinPropertyEntry(new PropertyEntry(declarationType, baseType, name, valueType, slot), label, id, root));
        byType.Clear();
        logInfo("Registered attached property " + label + " (id " + id + ") on " + baseType.FullName + ".");
    }

    public IReadOnlyList<ElinPropertyEntry> ForBaseType(Type type) {
        if (byType.TryGetValue(type, out ElinPropertyEntry[] cached)) {
            return cached;
        }

        List<ElinPropertyEntry> matches = [];
        for (int i = 0; i < entries.Count; i++) {
            if (entries[i].BaseType.IsAssignableFrom(type)) {
                matches.Add(entries[i]);
            }
        }

        ElinPropertyEntry[] array = matches.ToArray();
        byType[type] = array;
        return array;
    }
}
