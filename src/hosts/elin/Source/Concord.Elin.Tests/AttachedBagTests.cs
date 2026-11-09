using System;
using System.Collections.Generic;
using Concord.AttachedData;
using Concord.Elin;
using Xunit;

namespace Concord.Elin.Tests;

public class AttachedBagTests {
    private enum Mood { Calm, Angry }

    private sealed class Marker {
        public int Count = 0;
    }

    private sealed class Tally {
        public Dictionary<string, int> Counts = new Dictionary<string, int>();
    }

    private sealed class MemorySlot : IAttachedSlot {
        private readonly Dictionary<object, object> values = new Dictionary<object, object>();
        private readonly object fallback;

        public MemorySlot(object fallback) {
            this.fallback = fallback;
        }

        public object Get(object target) {
            return values.TryGetValue(target, out object v) ? v : fallback;
        }

        public void Set(object target, object value) {
            values[target] = value;
        }
    }

    private sealed class ThrowingSlot : IAttachedSlot {
        public object Get(object target) {
            throw new InvalidOperationException("slot get failed");
        }

        public void Set(object target, object value) {
            throw new InvalidOperationException("slot set failed");
        }
    }

    private static ElinPropertyEntry Entry(string key, Type valueType, IAttachedSlot slot, BagRoot root) {
        return new ElinPropertyEntry(new Concord.Orchestration.PropertyEntry(typeof(object), typeof(object), key, valueType, slot), "concord.t." + key, AttachedKeys.Id("concord.t." + key), root);
    }

    [Fact]
    public void WriteObj_SkipsDefaultsAndClearsStaleKeys() {
        object target = new object();
        MemorySlot slot = new MemorySlot(0);
        ElinPropertyEntry entry = Entry("visits", typeof(int), slot, BagRoot.Card);
        Dictionary<int, object> bag = new Dictionary<int, object> { [entry.Id] = 5 };

        AttachedBag.WriteObj(target, new[] { entry }, (id, v) => { if (v == null) { bag.Remove(id); } else { bag[id] = v; } }, _ => { });

        Assert.False(bag.ContainsKey(entry.Id));
    }

    [Fact]
    public void WriteObj_WritesNonDefault() {
        object target = new object();
        MemorySlot slot = new MemorySlot(0);
        slot.Set(target, 3);
        ElinPropertyEntry entry = Entry("visits", typeof(int), slot, BagRoot.Card);
        Dictionary<int, object> bag = new Dictionary<int, object>();

        AttachedBag.WriteObj(target, new[] { entry }, (id, v) => bag[id] = v, _ => { });

        Assert.Equal(3, bag[entry.Id]);
    }

    [Fact]
    public void WriteObj_SkipsSpatialEntries() {
        object target = new object();
        MemorySlot slot = new MemorySlot(0);
        slot.Set(target, 3);
        ElinPropertyEntry entry = Entry("flag", typeof(int), slot, BagRoot.Spatial);
        Dictionary<int, object> bag = new Dictionary<int, object>();

        AttachedBag.WriteObj(target, new[] { entry }, (id, v) => bag[id] = v, _ => { });

        Assert.Empty(bag);
    }

    [Fact]
    public void ReadObj_CoercesInt64() {
        object target = new object();
        MemorySlot slot = new MemorySlot(0);
        ElinPropertyEntry entry = Entry("visits", typeof(int), slot, BagRoot.Card);
        Dictionary<int, object> bag = new Dictionary<int, object> { [entry.Id] = 9L };

        AttachedBag.ReadObj(target, new[] { entry }, id => bag.TryGetValue(id, out object v) ? v : null, _ => { });

        Assert.Equal(9, slot.Get(target));
    }

    [Fact]
    public void ReadObj_MissingKeyBecomesNull() {
        object target = new object();
        MemorySlot slot = new MemorySlot(null);
        slot.Set(target, new Marker());
        ElinPropertyEntry entry = Entry("marker", typeof(Marker), slot, BagRoot.Card);

        AttachedBag.ReadObj(target, new[] { entry }, id => null, _ => { });

        Assert.Null(slot.Get(target));
    }

    [Fact]
    public void ReadObj_LogsAndContinuesOnThrow() {
        object target = new object();
        ElinPropertyEntry bad = Entry("bad", typeof(int), new ThrowingSlot(), BagRoot.Card);
        MemorySlot good = new MemorySlot(0);
        ElinPropertyEntry ok = Entry("ok", typeof(int), good, BagRoot.Card);
        List<string> log = new List<string>();
        Dictionary<int, object> bag = new Dictionary<int, object> { [bad.Id] = "not a number", [ok.Id] = 2L };

        AttachedBag.ReadObj(target, new[] { bad, ok }, id => bag.TryGetValue(id, out object v) ? v : null, log.Add);

        Assert.Single(log);
        Assert.Equal(2, good.Get(target));
    }

    [Fact]
    public void WriteInt_MapsBoolAndEnum() {
        object target = new object();
        MemorySlot flag = new MemorySlot(false);
        flag.Set(target, true);
        MemorySlot mood = new MemorySlot(Mood.Calm);
        mood.Set(target, Mood.Angry);
        ElinPropertyEntry f = Entry("flag", typeof(bool), flag, BagRoot.Spatial);
        ElinPropertyEntry m = Entry("mood", typeof(Mood), mood, BagRoot.Spatial);
        Dictionary<int, int> bag = new Dictionary<int, int>();

        AttachedBag.WriteInt(target, new[] { f, m }, (id, v) => bag[id] = v, _ => { });

        Assert.Equal(1, bag[f.Id]);
        Assert.Equal(1, bag[m.Id]);
    }

    [Fact]
    public void WriteInt_SkipsCardEntries() {
        object target = new object();
        MemorySlot slot = new MemorySlot(0);
        slot.Set(target, 3);
        ElinPropertyEntry entry = Entry("visits", typeof(int), slot, BagRoot.Card);
        Dictionary<int, int> bag = new Dictionary<int, int>();

        AttachedBag.WriteInt(target, new[] { entry }, (id, v) => bag[id] = v, _ => { });

        Assert.Empty(bag);
    }

    [Fact]
    public void ReadInt_MapsBackToBoolAndEnum() {
        object target = new object();
        MemorySlot flag = new MemorySlot(false);
        MemorySlot mood = new MemorySlot(Mood.Calm);
        ElinPropertyEntry f = Entry("flag", typeof(bool), flag, BagRoot.Spatial);
        ElinPropertyEntry m = Entry("mood", typeof(Mood), mood, BagRoot.Spatial);
        Dictionary<int, int> bag = new Dictionary<int, int> { [f.Id] = 1, [m.Id] = 1 };

        AttachedBag.ReadInt(target, new[] { f, m }, id => bag.TryGetValue(id, out int v) ? v : 0, _ => { });

        Assert.Equal(true, flag.Get(target));
        Assert.Equal(Mood.Angry, mood.Get(target));
    }

    [Fact]
    public void ReadInt_LogsAndContinuesOnThrow() {
        object target = new object();
        ElinPropertyEntry bad = Entry("bad", typeof(int), new ThrowingSlot(), BagRoot.Spatial);
        MemorySlot good = new MemorySlot(0);
        ElinPropertyEntry ok = Entry("ok", typeof(int), good, BagRoot.Spatial);
        List<string> log = new List<string>();
        Dictionary<int, int> bag = new Dictionary<int, int> { [ok.Id] = 2 };

        AttachedBag.ReadInt(target, new[] { bad, ok }, id => bag.TryGetValue(id, out int v) ? v : 0, log.Add);

        Assert.Single(log);
        Assert.Equal(2, good.Get(target));
    }

    [Fact]
    public void Copy_MovesValuesBetweenTargets() {
        object source = new object();
        object result = new object();
        MemorySlot slot = new MemorySlot(0);
        slot.Set(source, 4);
        ElinPropertyEntry entry = Entry("visits", typeof(int), slot, BagRoot.Card);

        AttachedBag.Copy(source, result, new[] { entry }, _ => { });

        Assert.Equal(4, slot.Get(result));
    }

    [Fact]
    public void Copy_DeepCopiesClassValuesAndPassesPrimitivesThrough() {
        object source = new object();
        object result = new object();
        MemorySlot markers = new MemorySlot(null);
        Marker original = new Marker { Count = 7 };
        markers.Set(source, original);
        MemorySlot visits = new MemorySlot(0);
        visits.Set(source, 4);
        ElinPropertyEntry marker = Entry("marker", typeof(Marker), markers, BagRoot.Card);
        ElinPropertyEntry visit = Entry("visits", typeof(int), visits, BagRoot.Card);

        AttachedBag.Copy(source, result, new[] { marker, visit }, _ => { });

        Marker copied = Assert.IsType<Marker>(markers.Get(result));
        Assert.NotSame(original, copied);
        Assert.Equal(7, copied.Count);
        Assert.Equal(4, visits.Get(result));
    }

    [Fact]
    public void Copy_LogsAndContinuesOnThrow() {
        object source = new object();
        object result = new object();
        ElinPropertyEntry bad = Entry("bad", typeof(int), new ThrowingSlot(), BagRoot.Card);
        MemorySlot good = new MemorySlot(0);
        good.Set(source, 4);
        ElinPropertyEntry ok = Entry("ok", typeof(int), good, BagRoot.Card);
        List<string> log = new List<string>();

        AttachedBag.Copy(source, result, new[] { bad, ok }, log.Add);

        Assert.Single(log);
        Assert.Equal(4, good.Get(result));
    }

    [Fact]
    public void Equal_ComparesClassValuesByContent() {
        object a = new object();
        object b = new object();
        object c = new object();
        MemorySlot markers = new MemorySlot(null);
        markers.Set(a, new Marker { Count = 7 });
        ElinPropertyEntry entry = Entry("marker", typeof(Marker), markers, BagRoot.Card);

        AttachedBag.Copy(a, b, new[] { entry }, _ => { });

        Assert.NotSame(markers.Get(a), markers.Get(b));
        Assert.True(AttachedBag.Equal(a, b, new[] { entry }, _ => { }));

        markers.Set(c, new Marker { Count = 8 });
        Assert.False(AttachedBag.Equal(a, c, new[] { entry }, _ => { }));
    }

    [Fact]
    public void Equal_IgnoresMemberOrderInClassValues() {
        object a = new object();
        object b = new object();
        MemorySlot tallies = new MemorySlot(null);
        Tally first = new Tally();
        first.Counts["x"] = 1;
        first.Counts["y"] = 2;
        Tally second = new Tally();
        second.Counts["y"] = 2;
        second.Counts["x"] = 1;
        tallies.Set(a, first);
        tallies.Set(b, second);
        ElinPropertyEntry entry = Entry("tally", typeof(Tally), tallies, BagRoot.Card);

        Assert.True(AttachedBag.Equal(a, b, new[] { entry }, _ => { }));
    }

    [Fact]
    public void Equal_LogsAndReturnsFalseWhenSlotThrows() {
        object a = new object();
        object b = new object();
        ElinPropertyEntry entry = Entry("marker", typeof(Marker), new ThrowingSlot(), BagRoot.Card);
        List<string> log = new List<string>();

        Assert.False(AttachedBag.Equal(a, b, new[] { entry }, log.Add));
        Assert.Single(log);
    }

    [Fact]
    public void Equal_ComparesEveryEntry() {
        object a = new object();
        object b = new object();
        MemorySlot slot = new MemorySlot(0);
        slot.Set(a, 1);
        slot.Set(b, 1);
        ElinPropertyEntry entry = Entry("visits", typeof(int), slot, BagRoot.Card);

        Assert.True(AttachedBag.Equal(a, b, new[] { entry }, _ => { }));
        slot.Set(b, 2);
        Assert.False(AttachedBag.Equal(a, b, new[] { entry }, _ => { }));
    }
}
