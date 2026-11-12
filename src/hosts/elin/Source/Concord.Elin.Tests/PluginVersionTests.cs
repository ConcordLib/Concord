using System.Reflection;
using BepInEx;
using Xunit;

namespace Concord.Elin.Tests;

public sealed class PluginVersionTests {
    [Fact]
    public void ThePluginReportsTheVersionTheBuildStamped() {
        BepInPlugin plugin = typeof(Plugin).GetCustomAttribute<BepInPlugin>();
        string stamped = typeof(Plugin).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;

        Assert.Equal(stamped.Split('+')[0], plugin.Version.ToString());
    }
}
