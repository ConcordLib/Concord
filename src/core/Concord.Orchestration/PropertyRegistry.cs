using Concord.AttachedData;
using Concord.Backend;

namespace Concord.Orchestration;

/// <summary>
///     Checks an attached property a mod registers by hand and hands it to the same fan-out
///     attribute declarations go through, so install order does not matter. A refusal goes to the log.
/// </summary>
public static class PropertyRegistry {
    /// <summary>
    ///     Registers one attached property.
    /// </summary>
    /// <param name="declarationType">The type that declares the property. Its assembly names the mod to blame.</param>
    /// <param name="baseType">The target type the property attaches to.</param>
    /// <param name="key">The field name, as the mod spelled it in source.</param>
    /// <param name="valueType">The field type as declared, not the runtime type of a stored value.</param>
    /// <param name="slot">The storage the patched code reads and writes.</param>
    public static void Register(Type declarationType, Type baseType, string key, Type valueType, IAttachedSlot slot) {
        PropertyEntry entry = new PropertyEntry(declarationType, baseType, key, valueType, slot);
        string? refusal = Refuse(entry);
        if (refusal != null) {
            PatchLog.Write(Blame(entry) + " will not be registered: " + refusal);
            return;
        }

        Patcher.AttachedPropertyFanOut.RegisterAttachedProperty(
            entry.DeclarationType, entry.BaseType, entry.Key, entry.ValueType, entry.Slot);
    }

    private static string? Refuse(PropertyEntry entry) {
        if (string.IsNullOrWhiteSpace(entry.Key) || !IsUsableKey(entry.Key)) {
            return "'" + entry.Key + "' is not usable as a save key. Use letters, digits, '_' and '-', starting with a letter or '_'.";
        }

        if (!IsStorable(entry.ValueType)) {
            return (entry.ValueType?.FullName ?? "null") + " cannot be stored in a field.";
        }

        return null;
    }

    private static bool IsUsableKey(string key) {
        for (int i = 0; i < key.Length; i++) {
            char c = key[i];
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '-') {
                return false;
            }
        }

        return char.IsLetter(key[0]) || key[0] == '_';
    }

    private static bool IsStorable(Type valueType) {
        return valueType != null
               && valueType != typeof(void)
               && !valueType.IsByRef
               && !valueType.IsPointer
               && !valueType.ContainsGenericParameters;
    }

    private static string Blame(PropertyEntry entry) {
        return "Attached property '" + entry.DeclarationType.FullName + "." + entry.Key
               + "' from " + entry.DeclarationType.Assembly.GetName().Name;
    }
}
