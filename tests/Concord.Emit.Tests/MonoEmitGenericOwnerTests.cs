using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public sealed class MonoEmitGenericOwnerHolder<T> {
    public T Value;

    public MonoEmitGenericOwnerHolder(T value) {
        Value = value;
    }
}

internal static class MonoEmitGenericOwnerCallSites {
    public static int ReadsFieldOnClosedGeneric(MonoEmitGenericOwnerHolder<int> owner) {
        return owner.Value;
    }

    public static int ReadsRankThreeCorner(int[,,] cube) {
        return cube[0, 0, 0];
    }
}

public sealed class MonoEmitGenericOwnerTests {
    [Fact]
    public void AWrapper_ReadingAFieldOnAClosedGenericType_ReturnsTheTargetsValue() {
        MethodBase target = typeof(MonoEmitGenericOwnerCallSites).GetMethod(nameof(MonoEmitGenericOwnerCallSites.ReadsFieldOnClosedGeneric))!;
        ComposeResult result = WrapperComposer.Compose(target, []);

        MonoEmitGenericOwnerHolder<int> owner = new MonoEmitGenericOwnerHolder<int>(11);
        object? value = result.Wrapper.Invoke(null, [owner]);

        Assert.Equal(11, value);
    }

    [Fact]
    public void AWrapper_OnARankThreeArrayAccess_ReturnsTheTargetsValue() {
        MethodBase target = typeof(MonoEmitGenericOwnerCallSites).GetMethod(nameof(MonoEmitGenericOwnerCallSites.ReadsRankThreeCorner))!;
        ComposeResult result = WrapperComposer.Compose(target, []);

        object? value = result.Wrapper.Invoke(null, [new int[1, 1, 1]]);

        Assert.Equal(0, value);
    }
}
