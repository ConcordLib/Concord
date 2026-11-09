using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Concord.Elin;

public static class AttachedBag {
    public static void WriteObj(object target, IReadOnlyList<ElinPropertyEntry> entries, Action<int, object> setObj, Action<string> logError) {
        for (int i = 0; i < entries.Count; i++) {
            ElinPropertyEntry entry = entries[i];
            if (entry.Root != BagRoot.Card) {
                continue;
            }

            try {
                object value = entry.Slot.Get(target);
                setObj(entry.Id, ValueRules.IsDefault(value, entry.ValueType) ? null : value);
            }
            catch (Exception e) {
                logError("[Concord.Elin] Failed to write attached '" + entry.Label + "': " + e);
            }
        }
    }

    public static void ReadObj(object target, IReadOnlyList<ElinPropertyEntry> entries, Func<int, object> getObj, Action<string> logError) {
        for (int i = 0; i < entries.Count; i++) {
            ElinPropertyEntry entry = entries[i];
            if (entry.Root != BagRoot.Card) {
                continue;
            }

            try {
                entry.Slot.Set(target, ValueRules.Coerce(getObj(entry.Id), entry.ValueType));
            }
            catch (Exception e) {
                logError("[Concord.Elin] Failed to read attached '" + entry.Label + "': " + e);
            }
        }
    }

    public static void WriteInt(object target, IReadOnlyList<ElinPropertyEntry> entries, Action<int, int> setInt, Action<string> logError) {
        for (int i = 0; i < entries.Count; i++) {
            ElinPropertyEntry entry = entries[i];
            if (entry.Root != BagRoot.Spatial) {
                continue;
            }

            try {
                object value = entry.Slot.Get(target);
                setInt(entry.Id, value == null ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture));
            }
            catch (Exception e) {
                logError("[Concord.Elin] Failed to write attached '" + entry.Label + "': " + e);
            }
        }
    }

    public static void ReadInt(object target, IReadOnlyList<ElinPropertyEntry> entries, Func<int, int> getInt, Action<string> logError) {
        for (int i = 0; i < entries.Count; i++) {
            ElinPropertyEntry entry = entries[i];
            if (entry.Root != BagRoot.Spatial) {
                continue;
            }

            try {
                int stored = getInt(entry.Id);
                object value;
                if (entry.ValueType == typeof(bool)) {
                    value = stored != 0;
                }
                else if (entry.ValueType.IsEnum) {
                    value = Enum.ToObject(entry.ValueType, stored);
                }
                else {
                    value = stored;
                }

                entry.Slot.Set(target, value);
            }
            catch (Exception e) {
                logError("[Concord.Elin] Failed to read attached '" + entry.Label + "': " + e);
            }
        }
    }

    public static void Copy(object source, object result, IReadOnlyList<ElinPropertyEntry> entries, Action<string> logError) {
        for (int i = 0; i < entries.Count; i++) {
            ElinPropertyEntry entry = entries[i];
            try {
                entry.Slot.Set(result, Duplicate(entry.Slot.Get(source), entry.ValueType));
            }
            catch (Exception e) {
                logError("[Concord.Elin] Failed to copy attached '" + entry.Label + "': " + e);
            }
        }
    }

    internal static object Duplicate(object value, Type valueType) {
        if (value == null || valueType.IsValueType || valueType == typeof(string)) {
            return value;
        }

        Type runtimeType = value.GetType();
        return JsonConvert.DeserializeObject(JsonConvert.SerializeObject(value, runtimeType, IO.dpSetting), runtimeType, IO.dpSetting);
    }

    public static bool Equal(object a, object b, IReadOnlyList<ElinPropertyEntry> entries, Action<string> logError) {
        for (int i = 0; i < entries.Count; i++) {
            ElinPropertyEntry entry = entries[i];
            try {
                if (!ValueEqual(entry.Slot.Get(a), entry.Slot.Get(b), entry.ValueType)) {
                    return false;
                }
            }
            catch (Exception e) {
                logError("[Concord.Elin] Failed to compare attached '" + entry.Label + "': " + e);
                return false;
            }
        }

        return true;
    }

    internal static bool ValueEqual(object a, object b, Type valueType) {
        if (object.ReferenceEquals(a, b)) {
            return true;
        }

        if (a == null || b == null || valueType.IsValueType || valueType == typeof(string)) {
            return object.Equals(a, b);
        }

        Type runtimeType = a.GetType();
        if (runtimeType != b.GetType()) {
            return object.Equals(a, b);
        }

        JToken left = JToken.Parse(JsonConvert.SerializeObject(a, runtimeType, IO.dpSetting));
        JToken right = JToken.Parse(JsonConvert.SerializeObject(b, runtimeType, IO.dpSetting));
        return JToken.DeepEquals(left, right);
    }
}
