using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace Concord.Waylith.OffsetMap;

public static class Adapter {
    private static readonly Assembly Assembly = Find("Concord.Host.Adapter");

    private static readonly Type EntryPoint = Assembly?.GetType("Concord.Host.Adapter.AdapterEntryPoint");

    private static readonly Type Attribution = Assembly?.GetType("Concord.Host.Adapter.FrameAttribution");

    private static readonly Type Backend = Assembly?.GetType("Concord.Host.Adapter.WaylithDetourBackend");

    private static readonly Type DetourBackend = Find("Concord.Detour")?.GetType("Concord.Detour.DetourBackend");

    public static bool Present => Assembly != null;

    public static string Identity() {
        if (Assembly == null) {
            return "adapter-absent";
        }

        string path = Assembly.Location;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
            return "no-file";
        }

        try {
            using (FileStream stream = File.OpenRead(path)) {
                byte[] digest = MD5.Create().ComputeHash(stream);
                return BitConverter.ToString(digest).Replace("-", string.Empty).ToLowerInvariant();
            }
        } catch (Exception thrown) {
            return "THREW " + Cause(thrown).GetType().Name;
        }
    }

    public static string Selected() {
        if (EntryPoint == null) {
            return "adapter-absent";
        }

        object current = EntryPoint.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        if (current == null) {
            return "not-installed";
        }

        PropertyInfo waylith = current.GetType().GetProperty("Waylith");
        if (waylith == null) {
            return "property-missing";
        }

        return (bool)waylith.GetValue(current) ? "body-replacement" : "refused";
    }

    public static string SelectionReason() {
        if (EntryPoint == null) {
            return "adapter-absent";
        }

        object current = EntryPoint.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        if (current == null) {
            return "not-installed";
        }

        PropertyInfo reason = current.GetType().GetProperty("Reason");
        if (reason == null) {
            return "property-missing";
        }

        return Report.Flatten((string)reason.GetValue(current));
    }

    public static string EmptyMapAlways() {
        if (Backend == null) {
            return "adapter-absent";
        }

        try {
            PropertyInfo knob = Backend.GetProperty("EmptyMapAlways", BindingFlags.Public | BindingFlags.Static);
            if (knob == null) {
                return "property-missing";
            }

            return Convert.ToString(knob.GetValue(null), CultureInfo.InvariantCulture);
        } catch (Exception thrown) {
            return "THREW " + Cause(thrown).GetType().Name;
        }
    }

    public static bool Replaced(MethodBase target) {
        return Owners(target).Count > 0;
    }

    public static IReadOnlyList<string> Owners(MethodBase target) {
        if (Attribution == null) {
            return Array.Empty<string>();
        }

        try {
            return (IReadOnlyList<string>)Attribution
                .GetMethod("OwnersRegisteredFor", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { target });
        } catch (Exception) {
            return Array.Empty<string>();
        }
    }

    public static string OwnerAt(MethodBase target, int offset) {
        if (Attribution == null) {
            return "adapter-absent";
        }

        try {
            object owner = Attribution
                .GetMethod("OwnerAt", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { target, offset });
            return owner == null ? "<null>" : (string)owner;
        } catch (Exception thrown) {
            return "THREW " + Cause(thrown).GetType().Name;
        }
    }

    public static string OwnsBody(MethodBase target) {
        if (DetourBackend == null) {
            return "adapter-absent";
        }

        try {
            MethodInfo tryGetCurrent = DetourBackend.GetMethod("TryGetCurrent", BindingFlags.Public | BindingFlags.Static);
            if (tryGetCurrent == null) {
                return "method-missing";
            }

            object[] args = new object[] { null };
            bool installed = (bool)tryGetCurrent.Invoke(null, args);
            if (!installed) {
                return "not-installed";
            }

            object backend = args[0];
            MethodInfo ownsBody = backend.GetType().GetMethod("OwnsBody", BindingFlags.Public | BindingFlags.Instance);
            if (ownsBody == null) {
                return "method-missing";
            }

            return (bool)ownsBody.Invoke(backend, new object[] { target }) ? "true" : "false";
        } catch (Exception thrown) {
            return "THREW " + Cause(thrown).GetType().Name;
        }
    }

    private static Exception Cause(Exception thrown) {
        return (thrown as TargetInvocationException)?.InnerException ?? thrown;
    }

    private static Assembly Find(string name) {
        foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies()) {
            if (loaded.GetName().Name == name) {
                return loaded;
            }
        }

        return null;
    }
}
