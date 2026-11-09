using System;
using Concord.Elin;
using Xunit;

namespace Concord.Elin.Tests;

public class ValueRulesTests {
    private enum Mood { Calm, Angry }

    private sealed class Marker {
        public int Count = 0;
    }

    private sealed class NoCtor {
        public NoCtor(int x) {
        }
    }

    [Fact]
    public void RootOf_CardSubtypes() {
        Assert.Equal(BagRoot.Card, ValueRules.RootOf(typeof(Chara)));
        Assert.Equal(BagRoot.Card, ValueRules.RootOf(typeof(Thing)));
        Assert.Equal(BagRoot.Spatial, ValueRules.RootOf(typeof(Zone)));
        Assert.Equal(BagRoot.None, ValueRules.RootOf(typeof(Marker)));
    }

    [Theory]
    [InlineData(typeof(int))]
    [InlineData(typeof(bool))]
    [InlineData(typeof(float))]
    [InlineData(typeof(long))]
    [InlineData(typeof(string))]
    [InlineData(typeof(Mood))]
    [InlineData(typeof(Marker))]
    public void Refuse_AcceptsCardValues(Type type) {
        Assert.Null(ValueRules.Refuse(BagRoot.Card, type));
    }

    [Theory]
    [InlineData(typeof(int?))]
    [InlineData(typeof(decimal))]
    [InlineData(typeof(ulong))]
    [InlineData(typeof(int[]))]
    [InlineData(typeof(Guid))]
    [InlineData(typeof(NoCtor))]
    [InlineData(typeof(IntPtr))]
    public void Refuse_RejectsUnsupportedCardValues(Type type) {
        Assert.NotNull(ValueRules.Refuse(BagRoot.Card, type));
    }

    [Theory]
    [InlineData(typeof(int))]
    [InlineData(typeof(bool))]
    [InlineData(typeof(Mood))]
    public void Refuse_AcceptsSpatialInts(Type type) {
        Assert.Null(ValueRules.Refuse(BagRoot.Spatial, type));
    }

    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(float))]
    [InlineData(typeof(Marker))]
    public void Refuse_RejectsSpatialNonInts(Type type) {
        Assert.NotNull(ValueRules.Refuse(BagRoot.Spatial, type));
    }

    [Fact]
    public void Coerce_Int64ToInt() {
        Assert.Equal(7, ValueRules.Coerce(7L, typeof(int)));
    }

    [Fact]
    public void Coerce_DoubleToFloat() {
        Assert.Equal(1.5f, ValueRules.Coerce(1.5d, typeof(float)));
    }

    [Fact]
    public void Coerce_Int64ToEnum() {
        Assert.Equal(Mood.Angry, ValueRules.Coerce(1L, typeof(Mood)));
    }

    [Fact]
    public void Coerce_BoolPassesThrough() {
        Assert.Equal(true, ValueRules.Coerce(true, typeof(bool)));
    }

    [Fact]
    public void Coerce_ClassInstancePassesThrough() {
        Marker marker = new Marker();
        Assert.Same(marker, ValueRules.Coerce(marker, typeof(Marker)));
    }

    [Fact]
    public void Coerce_OutOfRangeInt64BecomesNull() {
        Assert.Null(ValueRules.Coerce(5000000000L, typeof(int)));
    }

    [Fact]
    public void Coerce_UnparsableStringBecomesNull() {
        Assert.Null(ValueRules.Coerce("hello", typeof(int)));
        Assert.Null(ValueRules.Coerce("hello", typeof(Mood)));
    }

    [Fact]
    public void Coerce_WrongClassBecomesNull() {
        Assert.Null(ValueRules.Coerce("nope", typeof(Marker)));
    }

    [Fact]
    public void Coerce_PlaceholderBecomesNull() {
        Assert.Null(ValueRules.Coerce(new UnknownTypePlaceholder(), typeof(Marker)));
    }

    [Fact]
    public void Coerce_NullStaysNull() {
        Assert.Null(ValueRules.Coerce(null, typeof(int)));
    }

    [Fact]
    public void IsDefault_ValueTypes() {
        Assert.True(ValueRules.IsDefault(0, typeof(int)));
        Assert.False(ValueRules.IsDefault(1, typeof(int)));
        Assert.True(ValueRules.IsDefault(false, typeof(bool)));
        Assert.True(ValueRules.IsDefault(Mood.Calm, typeof(Mood)));
        Assert.True(ValueRules.IsDefault(null, typeof(string)));
        Assert.False(ValueRules.IsDefault("", typeof(string)));
        Assert.True(ValueRules.IsDefault(null, typeof(Marker)));
    }
}
