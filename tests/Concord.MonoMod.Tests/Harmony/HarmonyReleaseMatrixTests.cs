using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Concord.Backend;
using Concord.MonoMod;
using Concord.MonoMod.Harmony;
using HarmonyLib;
using Xunit;

namespace Concord.MonoMod.Tests;

[Collection("HarmonySerial")]
public sealed class HarmonyReleaseMatrixTests
{
#if NET472
    private static readonly string[] Frameworks = { "net472" };
#else
    private static readonly string[] Frameworks = { "net10.0", "net9.0", "net8.0", "net6.0" };
#endif

    private static readonly Dictionary<string, Assembly> Loaded = new Dictionary<string, Assembly>();

    [Theory]
    [InlineData("2.2.2", false)]
    [InlineData("2.3.3", true)]
    [InlineData("2.3.6", true)]
    [InlineData("2.4.2", true)]
    [InlineData("2.4.3", true)]
    [InlineData("3.0.0-rc.4", true)]
    public void TheDetourFactorySeamIsPresentOnlyWhereTheTableClaimsIt(string package, bool carriesFactory)
    {
        Type? factory = Release(package).GetType("MonoMod.Core.DetourFactory");

        Assert.Equal(carriesFactory, factory != null);
    }

    [Theory]
    [InlineData("2.2.2", false)]
    [InlineData("2.3.3", false)]
    [InlineData("2.3.6", false)]
    [InlineData("2.4.2", true)]
    [InlineData("2.4.3", true)]
    [InlineData("3.0.0-rc.4", true)]
    public void SetCurrentFactoryIsPresentOnlyWhereTheTableClaimsIt(string package, bool carriesSetter)
    {
        Type? factory = Release(package).GetType("MonoMod.Core.DetourFactory");
        MethodInfo? setter = factory?.GetMethod("SetCurrentFactory", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        Assert.Equal(carriesSetter, setter != null);
    }

    [Theory]
    [InlineData("2.2.2", false, false)]
    [InlineData("2.3.3", false, false)]
    [InlineData("2.3.6", false, false)]
    [InlineData("2.4.2", true, false)]
    [InlineData("2.4.3", true, false)]
    [InlineData("3.0.0-rc.4", true, true)]
    public void TheInnerPatchMembersTheReadersLookForAreAbsentOnTheOlderReleases(string package, bool carriesInfix, bool carriesInnerFinalizers)
    {
        Assembly copy = Release(package);
        Type patches = copy.GetType("HarmonyLib.Patches") ?? throw new InvalidOperationException("Harmony " + package + " carries no HarmonyLib.Patches");
        Type patch = copy.GetType("HarmonyLib.Patch") ?? throw new InvalidOperationException("Harmony " + package + " carries no HarmonyLib.Patch");

        Assert.Equal(carriesInfix, HasPublicMember(patches, "InnerPrefixes"));
        Assert.Equal(carriesInfix, HasPublicMember(patches, "InnerPostfixes"));
        Assert.Equal(carriesInfix, patch.GetField("innerMethod") != null);
        Assert.Equal(carriesInnerFinalizers, HasPublicMember(patches, "InnerFinalizers"));
    }

    private static bool HasPublicMember(Type type, string name)
    {
        return type.GetField(name, BindingFlags.Public | BindingFlags.Instance) != null
               || type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance) != null;
    }

    private static Assembly Release(string package)
    {
        lock (Loaded)
        {
            if (Loaded.TryGetValue(package, out Assembly? already))
            {
                return already;
            }

            Assembly copy = Assembly.Load(AssemblyImage.WithFreshModuleId(File.ReadAllBytes(ReleasePath(package))));
            Loaded[package] = copy;
            return copy;
        }
    }

    internal static string ReleasePath(string package)
    {
        string lib = Path.Combine(PackageRoot(), "lib.harmony", package, "lib");
        foreach (string framework in Frameworks)
        {
            string dll = Path.Combine(lib, framework, "0Harmony.dll");
            if (File.Exists(dll))
            {
                return dll;
            }
        }

        throw new FileNotFoundException("Lib.Harmony " + package + " carries none of " + string.Join(", ", Frameworks) + " under " + lib
                                        + ". The test project downloads it, so run a restore.");
    }

    private static string PackageRoot()
    {
        foreach (AssemblyMetadataAttribute metadata in typeof(HarmonyReleaseMatrixTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (metadata.Key == "NuGetPackageRoot" && !string.IsNullOrWhiteSpace(metadata.Value))
            {
                return metadata.Value!;
            }
        }

        throw new InvalidOperationException("the test assembly carries no NuGetPackageRoot metadata, so the Harmony releases on disk cannot be found");
    }
}
