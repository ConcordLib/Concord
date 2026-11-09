using Concord.Elin;
using Xunit;

namespace Concord.Elin.Tests;

public class AttachedKeysTests {
    [Fact]
    public void Label_UsesAssemblyNameAndField() {
        string label = AttachedKeys.Label(typeof(AttachedKeysTests), "visits");
        Assert.Equal("concord.Concord.Elin.Tests.visits", label);
    }

    [Fact]
    public void Id_IsNegative() {
        Assert.True(AttachedKeys.Id("concord.a.b") < 0);
        Assert.True(AttachedKeys.Id("concord.zzz.yyy") < 0);
    }

    [Fact]
    public void Id_IsStable() {
        Assert.Equal(AttachedKeys.Id("concord.a.b"), AttachedKeys.Id("concord.a.b"));
    }

    [Fact]
    public void Id_DiffersForDifferentLabels() {
        Assert.NotEqual(AttachedKeys.Id("concord.a.b"), AttachedKeys.Id("concord.a.c"));
    }

    [Fact]
    public void Id_MatchesKnownFnv1a() {
        Assert.Equal(unchecked((int)(0x811C9DC5u | 0x80000000u)), AttachedKeys.Id(""));
    }
}
