using System;
using System.Globalization;
using System.Reflection;

namespace Concord.Elin;

public enum BagRoot {
    None,
    Card,
    Spatial,
}

public static class ValueRules {
    public static BagRoot RootOf(Type baseType) {
        if (typeof(Card).IsAssignableFrom(baseType)) {
            return BagRoot.Card;
        }

        if (typeof(Spatial).IsAssignableFrom(baseType)) {
            return BagRoot.Spatial;
        }

        return BagRoot.None;
    }

    public static string Refuse(BagRoot root, Type valueType) {
        if (root == BagRoot.Spatial) {
            if (valueType == typeof(int) || valueType == typeof(bool) || valueType.IsEnum) {
                return null;
            }

            return "Spatial attached values must be int, bool or an enum, got " + valueType.FullName;
        }

        if (valueType.IsEnum || valueType == typeof(string)) {
            return null;
        }

        if (valueType.IsPrimitive) {
            if (valueType == typeof(ulong) || valueType == typeof(IntPtr) || valueType == typeof(UIntPtr)) {
                return "Attached value type " + valueType.FullName + " does not round-trip through json";
            }

            return null;
        }

        if (valueType.IsValueType) {
            return "Attached value type " + valueType.FullName + " is a struct or nullable. Use a primitive, enum, string or class.";
        }

        if (valueType.IsArray) {
            return "Attached value type " + valueType.FullName + " is an array. Wrap it in a class.";
        }

        if (valueType.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null) == null) {
            return "Attached value type " + valueType.FullName + " has no public parameterless constructor";
        }

        return null;
    }

    public static object Coerce(object raw, Type valueType) {
        if (raw == null || raw is UnknownTypePlaceholder) {
            return null;
        }

        if (valueType.IsEnum || valueType.IsPrimitive) {
            try {
                if (valueType.IsEnum) {
                    return Enum.ToObject(valueType, Convert.ToInt64(raw, CultureInfo.InvariantCulture));
                }

                return Convert.ChangeType(raw, valueType, CultureInfo.InvariantCulture);
            }
            catch (Exception e) when (e is InvalidCastException || e is FormatException || e is OverflowException || e is ArgumentException) {
                return null;
            }
        }

        return valueType.IsInstanceOfType(raw) ? raw : null;
    }

    public static bool IsDefault(object value, Type valueType) {
        if (value == null) {
            return true;
        }

        if (!valueType.IsValueType) {
            return false;
        }

        return value.Equals(Activator.CreateInstance(valueType));
    }
}
