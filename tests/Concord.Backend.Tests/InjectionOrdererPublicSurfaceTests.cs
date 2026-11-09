using System.Reflection;
using Xunit;

namespace Concord.Backend.Tests;

public class InjectionOrdererPublicSurfaceTests {
    [Fact]
    public void OrderForCompositionIsPublicApi() {
        Assert.True(typeof(InjectionOrderer).IsPublic);
        MethodInfo? method = typeof(InjectionOrderer).GetMethod("OrderForComposition", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
    }
}
