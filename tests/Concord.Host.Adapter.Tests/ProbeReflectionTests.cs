using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Concord.Detour;
using Concord.Host.Adapter;
using Xunit;

namespace Concord.Host.Adapter.Tests;

public sealed class ProbeReflectionTests {
    private const BindingFlags Statics = BindingFlags.Public | BindingFlags.Static;

    private const BindingFlags Instances = BindingFlags.Public | BindingFlags.Instance;

    private static readonly Regex StaticLookup = new Regex(
        @"\b(Backend|EntryPoint|Attribution|DetourBackend)\s*\??\.\s*Get(Property|Method)\(""(\w+)""",
        RegexOptions.Singleline);

    private static readonly Regex InstanceLookup = new Regex(
        @"\b(\w+)\s*\??\.\s*GetType\(\)\s*\??\.\s*Get(Property|Method)\(""(\w+)""",
        RegexOptions.Singleline);

    private static readonly Regex TypeLookup = new Regex(
        @"\bGetType\(""([\w.]+)""\)",
        RegexOptions.Singleline);

    [Fact]
    public void EveryReflectiveLookupInTheProbeAdapterResolvesOnTheAdapter() {
        string source = Source();
        Dictionary<string, (Type Type, BindingFlags Flags)> receivers = Receivers();

        List<(string Receiver, string Kind, string Member)> lookups = Matches(StaticLookup, source)
            .Concat(Matches(InstanceLookup, source))
            .Distinct()
            .ToList();

        Assert.NotEmpty(lookups);
        Assert.Contains(("Backend", "Property", "EmptyMapAlways"), lookups);
        Assert.Contains(("current", "Property", "Waylith"), lookups);
        Assert.Contains(("current", "Property", "Reason"), lookups);
        Assert.Contains(("DetourBackend", "Method", "TryGetCurrent"), lookups);
        Assert.Contains(("backend", "Method", "OwnsBody"), lookups);

        List<string> unmapped = lookups
            .Select(lookup => lookup.Receiver)
            .Where(receiver => !receivers.ContainsKey(receiver))
            .Distinct()
            .ToList();
        Assert.Empty(unmapped);

        List<string> missing = new List<string>();
        foreach ((string receiver, string kind, string member) in lookups) {
            (Type type, BindingFlags flags) = receivers[receiver];
            MemberInfo? found = kind == "Property"
                ? type.GetProperty(member, flags)
                : type.GetMethod(member, flags);
            if (found == null) {
                missing.Add($"{type.Name}.{member} ({kind})");
            }
        }

        Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();
        foreach (string name in TypeLookup.Matches(source).Select(match => match.Groups[1].Value).Distinct()) {
            if (!loaded.Any(assembly => assembly.GetType(name) != null)) {
                missing.Add($"{name} (Type)");
            }
        }

        Assert.Empty(missing);
    }

    private static Dictionary<string, (Type Type, BindingFlags Flags)> Receivers() {
        PropertyInfo? current = typeof(AdapterEntryPoint).GetProperty("Current", Statics);
        Assert.NotNull(current);

        return new Dictionary<string, (Type, BindingFlags)>(StringComparer.Ordinal) {
            ["Backend"] = (typeof(WaylithDetourBackend), Statics),
            ["EntryPoint"] = (typeof(AdapterEntryPoint), Statics),
            ["Attribution"] = (typeof(FrameAttribution), Statics),
            ["current"] = (current.PropertyType, Instances),
            ["DetourBackend"] = (typeof(DetourBackend), Statics),
            ["backend"] = (typeof(WaylithDetourBackend), Instances),
        };
    }

    private static IEnumerable<(string Receiver, string Kind, string Member)> Matches(Regex pattern, string source) {
        return pattern.Matches(source)
            .Select(match => (match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value));
    }

    private static string Source() {
        return File.ReadAllText(Path.Combine(
            Path.GetDirectoryName(Here())!, "Integration", "offsetmap", "Source", "Adapter.cs"));
    }

    private static string Here([CallerFilePath] string path = "") {
        return path;
    }
}
