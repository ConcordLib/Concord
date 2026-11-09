using System;
using System.Collections.Generic;

namespace Concord.Elin;

internal static class SaveHooks {
    internal static Action<string> Log = message => UnityEngine.Debug.LogError(message);

    internal static void WriteCard(Card card) {
        IReadOnlyList<ElinPropertyEntry> entries = Entries(card);
        if (entries == null) {
            return;
        }

        AttachedBag.WriteObj(card, entries, card.SetObj, Log);
    }

    internal static void ReadCard(Card card) {
        IReadOnlyList<ElinPropertyEntry> entries = Entries(card);
        if (entries == null) {
            return;
        }

        Dictionary<int, object> bag = card.mapObj;
        AttachedBag.ReadObj(card, entries, id => bag != null && bag.TryGetValue(id, out object v) ? v : null, Log);
    }

    internal static void WriteSpatial(Spatial spatial) {
        IReadOnlyList<ElinPropertyEntry> entries = Entries(spatial);
        if (entries == null) {
            return;
        }

        AttachedBag.WriteInt(spatial, entries, spatial.SetInt, Log);
    }

    internal static void ReadSpatial(Spatial spatial) {
        IReadOnlyList<ElinPropertyEntry> entries = Entries(spatial);
        if (entries == null) {
            return;
        }

        Dictionary<int, int> bag = spatial.mapInt;
        AttachedBag.ReadInt(spatial, entries, id => bag != null && bag.TryGetValue(id, out int v) ? v : 0, Log);
    }

    internal static void CopyCard(Card source, Card result) {
        IReadOnlyList<ElinPropertyEntry> entries = Entries(source);
        if (entries == null || result == null) {
            return;
        }

        AttachedBag.Copy(source, result, entries, Log);
    }

    internal static bool CardsEqual(Card a, Card b) {
        IReadOnlyList<ElinPropertyEntry> entries = Entries(a);
        if (entries == null || b == null) {
            return true;
        }

        return AttachedBag.Equal(a, b, entries, Log);
    }

    private static IReadOnlyList<ElinPropertyEntry> Entries(object target) {
        ElinAttachedPropertyRegistry registry = ElinRuntime.Registry;
        if (registry == null || registry.IsEmpty || target == null) {
            return null;
        }

        IReadOnlyList<ElinPropertyEntry> entries = registry.ForBaseType(target.GetType());
        return entries.Count == 0 ? null : entries;
    }
}
