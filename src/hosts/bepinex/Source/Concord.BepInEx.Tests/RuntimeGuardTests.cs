using System;
using System.IO;
using System.Reflection;
using Concord.BepInEx;
using Xunit;

namespace Concord.BepInEx.Tests;

public class RuntimeGuardTests {
    [Fact]
    public void Describe_IgnoresAssembliesNotNamedConcord() {
        Assembly self = typeof(RuntimeGuardTests).Assembly;
        Assert.Null(RuntimeGuard.Describe(self, Path.GetTempPath()));
    }

    [Fact]
    public void Describe_IgnoresConcordFromThePluginDir() {
        Assembly concord = typeof(Patcher).Assembly;
        string pluginDir = Path.GetDirectoryName(concord.Location);
        Assert.Null(RuntimeGuard.Describe(concord, pluginDir));
    }

    [Fact]
    public void Describe_ReportsConcordFromElsewhere() {
        Assembly concord = typeof(Patcher).Assembly;
        string message = RuntimeGuard.Describe(concord, Path.Combine(Path.GetTempPath(), "some-other-plugin"));
        Assert.NotNull(message);
        Assert.Contains(concord.Location, message);
        Assert.Contains("second Concord runtime", message);
    }
}
