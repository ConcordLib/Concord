using System;
using Concord.AttachedData;
using Concord.Orchestration;
using Xunit;

namespace Concord.RimWorld.Tests;

[Collection(GameAssemblies.Name)]
public sealed class RimWorldAttachedPropertyRegistryTests {
  private sealed class Target { }

  private sealed class ReplayTarget { }

  [Fact]
  public void RegisterAttachedProperty_NamespacesKeyWithDeclaringAssembly() {
    RimWorldAttachedPropertyRegistry registry = new RimWorldAttachedPropertyRegistry();

    IAttachedSlot slot = AttachedStorage.SlotAt(AttachedStorage.SlotFor(typeof(Target), "count", typeof(int)));
    registry.RegisterAttachedProperty(typeof(Target), typeof(Verse.Thing), "count", typeof(int), slot);

    System.Collections.Generic.IReadOnlyList<RimWorldAttachedPropertyRegistry.Entry> entries = registry.ForBaseType(typeof(Verse.Thing));
    Assert.Single(entries);
    Assert.Equal(typeof(Target).Assembly.GetName().Name + ".count", entries[0].Key);
  }

  [Fact]
  public void RegistrationMadeBeforeWire_IsDeliveredAfterWireThroughTheAdapter() {
    IAttachedSlot slot = AttachedStorage.SlotAt(AttachedStorage.SlotFor(typeof(ReplayTarget), "beforeWire", typeof(int)));
    PropertyRegistry.Register(typeof(ReplayTarget), typeof(Verse.Thing), "beforeWire", typeof(int), slot);

    RimWorldAttachedPropertyRegistry registry = new RimWorldAttachedPropertyRegistry();
    Patcher.UseAttachedPropertyRegistry(registry);

    System.Collections.Generic.IReadOnlyList<RimWorldAttachedPropertyRegistry.Entry> entries = registry.ForBaseType(typeof(Verse.Thing));
    Assert.Single(entries);
    Assert.Equal(typeof(ReplayTarget).Assembly.GetName().Name + ".beforeWire", entries[0].Key);
  }
}
