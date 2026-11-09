using System;
using System.Collections.Generic;
using Concord.AttachedData;
using Verse;
using Xunit;

namespace Concord.RimWorld.Tests;

[Collection(GameAssemblies.Name)]
public sealed class ScribersTests {
  private sealed class Holder : IExposable {
    public void ExposeData() {
      SaveHooks.ScribeAttached(this);
    }
  }

  private sealed class OtherHolder : IExposable {
    public void ExposeData() {
      SaveHooks.ScribeAttached(this);
    }
  }

  private static IAttachedSlot Slot(Type baseType, string key, Type valueType) {
    return AttachedStorage.SlotAt(AttachedStorage.SlotFor(baseType, key, valueType));
  }

  [Fact]
  public void Add_SameNameOnTwoTargets_GetsDistinctLabels() {
    RimWorldAttachedPropertyRegistry registry = new RimWorldAttachedPropertyRegistry();
    registry.Add(typeof(Holder), "Probe.count", typeof(int), Slot(typeof(Holder), "Probe.count", typeof(int)));
    registry.Add(typeof(OtherHolder), "Probe.count", typeof(int), Slot(typeof(OtherHolder), "Probe.count", typeof(int)));

    IReadOnlyList<RimWorldAttachedPropertyRegistry.Entry> first = registry.ForBaseType(typeof(Holder));
    IReadOnlyList<RimWorldAttachedPropertyRegistry.Entry> second = registry.ForBaseType(typeof(OtherHolder));

    Assert.NotEqual(first[0].ScribeLabel, second[0].ScribeLabel);
  }

  [Fact]
  public void Add_KeyThatIsNotAnXmlName_Throws() {
    RimWorldAttachedPropertyRegistry registry = new RimWorldAttachedPropertyRegistry();

    Assert.Throws<ArgumentException>(() => registry.Add(typeof(Holder), "My Mod.count", typeof(int), Slot(typeof(Holder), "My Mod.count", typeof(int))));
  }

  [Fact]
  public void Add_TypeScribeValuesCannotWrite_Throws() {
    Type[] unsupported = [typeof(List<int>), typeof(ThingDef), typeof(Thing)];

    for (int i = 0; i < unsupported.Length; i++) {
      Type valueType = unsupported[i];
      RimWorldAttachedPropertyRegistry registry = new RimWorldAttachedPropertyRegistry();
      string field = "Probe" + i;

      Assert.Throws<ArgumentException>(() => registry.Add(typeof(Holder), field, valueType, Slot(typeof(Holder), field, valueType)));
    }
  }
}
