using System;
using System.Collections.Generic;
using Concord.AttachedData;
using Concord.Elin;
using Xunit;

namespace Concord.Elin.Tests;

public class PropertyRegistryTests {
    private sealed class Slot : IAttachedSlot {
        public object Get(object target) {
            return null;
        }

        public void Set(object target, object value) {
        }
    }

    private sealed class OtherAssemblyMarker {
    }

    private static ElinAttachedPropertyRegistry NewRegistry(List<string> info, List<string> errors) {
        return new ElinAttachedPropertyRegistry(info.Add, _ => { }, errors.Add);
    }

    [Fact]
    public void Register_ThenForBaseTypeFindsSubtypeEntries() {
        ElinAttachedPropertyRegistry registry = NewRegistry([], []);
        registry.RegisterAttachedProperty(typeof(PropertyRegistryTests), typeof(Card), "visits", typeof(int), new Slot());

        IReadOnlyList<ElinPropertyEntry> entries = registry.ForBaseType(typeof(Chara));

        Assert.Single(entries);
        Assert.Equal(BagRoot.Card, entries[0].Root);
        Assert.Equal("concord.Concord.Elin.Tests.visits", entries[0].Label);
        Assert.Equal(AttachedKeys.Id("concord.Concord.Elin.Tests.visits"), entries[0].Id);
        Assert.True(entries[0].Id < 0);
    }

    [Fact]
    public void Register_WrapsTheCoreEntryAndKeepsItsFields() {
        ElinAttachedPropertyRegistry registry = NewRegistry([], []);
        Slot slot = new Slot();
        registry.RegisterAttachedProperty(typeof(PropertyRegistryTests), typeof(Card), "visits", typeof(int), slot);

        ElinPropertyEntry entry = registry.ForBaseType(typeof(Card))[0];

        Assert.Equal(typeof(Card), entry.BaseType);
        Assert.Equal("visits", entry.Key);
        Assert.Equal(typeof(int), entry.ValueType);
        Assert.Same(slot, entry.Slot);
    }

    [Fact]
    public void Register_LogsLabelAndId() {
        List<string> info = [];
        ElinAttachedPropertyRegistry registry = NewRegistry(info, []);
        registry.RegisterAttachedProperty(typeof(PropertyRegistryTests), typeof(Card), "visits", typeof(int), new Slot());

        string label = "concord.Concord.Elin.Tests.visits";
        Assert.Single(info);
        Assert.Contains(label, info[0]);
        Assert.Contains(AttachedKeys.Id(label).ToString(), info[0]);
    }

    [Fact]
    public void Register_RefusesDuplicateKeyAndLogs() {
        List<string> errors = [];
        ElinAttachedPropertyRegistry registry = NewRegistry([], errors);
        registry.RegisterAttachedProperty(typeof(PropertyRegistryTests), typeof(Card), "visits", typeof(int), new Slot());
        registry.RegisterAttachedProperty(typeof(PropertyRegistryTests), typeof(Card), "visits", typeof(int), new Slot());

        Assert.Single(registry.ForBaseType(typeof(Card)));
        Assert.Single(errors);
    }

    [Fact]
    public void Register_RefusesSameFieldNameOnDifferentBaseTypes() {
        List<string> errors = [];
        ElinAttachedPropertyRegistry registry = NewRegistry([], errors);
        registry.RegisterAttachedProperty(typeof(PropertyRegistryTests), typeof(Card), "visits", typeof(int), new Slot());
        registry.RegisterAttachedProperty(typeof(PropertyRegistryTests), typeof(Zone), "visits", typeof(int), new Slot());

        Assert.Empty(registry.ForBaseType(typeof(Zone)));
        Assert.Contains(errors, m => m.Contains("assembly-wide"));
    }

    [Fact]
    public void Register_AllowsSameFieldNameFromAnotherAssembly() {
        ElinAttachedPropertyRegistry registry = NewRegistry([], []);
        registry.RegisterAttachedProperty(typeof(PropertyRegistryTests), typeof(Card), "visits", typeof(int), new Slot());
        registry.RegisterAttachedProperty(typeof(Card), typeof(Card), "visits", typeof(int), new Slot());

        Assert.Equal(2, registry.ForBaseType(typeof(Card)).Count);
    }

    [Fact]
    public void Register_RefusesUnsupportedValueType() {
        List<string> errors = [];
        ElinAttachedPropertyRegistry registry = NewRegistry([], errors);
        registry.RegisterAttachedProperty(typeof(PropertyRegistryTests), typeof(Card), "n", typeof(int?), new Slot());

        Assert.Empty(registry.ForBaseType(typeof(Card)));
        Assert.Single(errors);
    }

    [Fact]
    public void Register_RefusesSpatialString() {
        List<string> errors = [];
        ElinAttachedPropertyRegistry registry = NewRegistry([], errors);
        registry.RegisterAttachedProperty(typeof(PropertyRegistryTests), typeof(Zone), "name", typeof(string), new Slot());

        Assert.Empty(registry.ForBaseType(typeof(Zone)));
        Assert.Single(errors);
    }

    [Fact]
    public void Register_WarnsAndKeepsAnUnsavedRootWithNoneBag() {
        List<string> warnings = [];
        ElinAttachedPropertyRegistry registry = new ElinAttachedPropertyRegistry(_ => { }, warnings.Add, _ => { });
        registry.RegisterAttachedProperty(typeof(PropertyRegistryTests), typeof(OtherAssemblyMarker), "x", typeof(int), new Slot());

        Assert.Equal(BagRoot.None, registry.ForBaseType(typeof(OtherAssemblyMarker))[0].Root);
        Assert.Single(warnings);
    }

    [Fact]
    public void ForBaseType_EmptyForUnrelatedType() {
        ElinAttachedPropertyRegistry registry = NewRegistry([], []);
        registry.RegisterAttachedProperty(typeof(PropertyRegistryTests), typeof(Card), "visits", typeof(int), new Slot());

        Assert.Empty(registry.ForBaseType(typeof(Zone)));
    }

    [Fact]
    public void IsEmpty_UntilSomethingRegisters() {
        ElinAttachedPropertyRegistry registry = NewRegistry([], []);
        Assert.True(registry.IsEmpty);

        registry.RegisterAttachedProperty(typeof(PropertyRegistryTests), typeof(Card), "visits", typeof(int), new Slot());

        Assert.False(registry.IsEmpty);
    }
}
