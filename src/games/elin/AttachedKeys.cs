using System;
using System.Text;

namespace Concord.Elin;

public static class AttachedKeys {
    public static string Label(Type declarationType, string fieldName) {
        return "concord." + declarationType.Assembly.GetName().Name + "." + fieldName;
    }

    public static int Id(string label) {
        uint hash = 2166136261u;
        byte[] bytes = Encoding.UTF8.GetBytes(label);
        for (int i = 0; i < bytes.Length; i++) {
            hash ^= bytes[i];
            hash *= 16777619u;
        }

        return unchecked((int)(hash | 0x80000000u));
    }
}
