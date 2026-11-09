using System;
using System.IO;
using System.Reflection;
using Concord.BepInEx;
using Xunit;

namespace Concord.BepInEx.Tests;

[Collection("HarmonySerial")]
public class HarmonyProbeTests {
    static HarmonyProbeTests() {
        typeof(HarmonyLib.Harmony).GetType();
    }

    private static string HarmonyDir() {
        return Path.GetDirectoryName(typeof(HarmonyLib.Harmony).Assembly.Location);
    }

    [Fact]
    public void FindCoreHarmony_ReturnsHarmonyUnderTheCoreDir() {
        Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();
        Assembly result = HarmonyProbe.FindCoreHarmony(() => loaded, HarmonyDir(), _ => { });
        Assert.NotNull(result);
    }

    [Fact]
    public void FindCoreHarmony_ReturnsNullWhenNotLoaded() {
        Assembly[] loaded = Array.FindAll(AppDomain.CurrentDomain.GetAssemblies(), a => a.GetName().Name != "0Harmony");
        Assembly result = HarmonyProbe.FindCoreHarmony(() => loaded, HarmonyDir(), _ => { });
        Assert.Null(result);
    }

    [Fact]
    public void FindCoreHarmony_ReturnsNullAndLogsWhenOutsideTheCoreDir() {
        Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();
        string logged = null;
        Assembly result = HarmonyProbe.FindCoreHarmony(() => loaded, Path.Combine(Path.GetTempPath(), "not-bepinex-core"), m => logged = m);
        Assert.Null(result);
        Assert.Contains("outside BepInEx/core", logged);
    }
}

[CollectionDefinition("HarmonySerial", DisableParallelization = true)]
public sealed class HarmonySerialCollection {
}
