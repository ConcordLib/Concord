using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Backend;
using Concord.Emit;
using Concord.RimWorld;
using Xunit;

namespace Concord.RimWorld.Tests;

[Collection(GameAssemblies.Name)]
public class AdapterWiringTests {
  [MethodImpl(MethodImplOptions.NoInlining)]
  private static void ProbeTarget() {
  }

  private static MethodBase ProbeMethod() {
    return typeof(AdapterWiringTests).GetMethod(nameof(ProbeTarget), BindingFlags.NonPublic | BindingFlags.Static);
  }

  private static Injection MakeInjection(MethodBase method) {
    return new Injection(method, new InjectAt.Head(), "test-owner", 0);
  }

  [Fact]
  public void Wire_InstallBackendSucceeds_RunsEagerTierAgainstTheInstalledBackendAndSetsReady() {
    FakeBackend backend = new FakeBackend();
    List<string> log = new List<string>();

    try {
      WireContext context = new WireContext {
        InstallBackend = () => {
          PatchBackend.Current = backend;
          return true;
        },
        Log = log.Add,
        ApplyEagerTier = () => {
          MethodBase target = ProbeMethod();
          IReadOnlyList<Injection> added = new List<Injection> { MakeInjection(target) };
          PatchBackend.Current.ApplyComposed(target, added);
        }
      };

      RimWorldAdapter.Wire(context);

      Assert.True(RimWorldAdapter.Ready);
      Assert.Equal(1, backend.ApplyComposedCallCount);
      Assert.Same(backend, PatchBackend.Current);
    } finally {
      RimWorldAdapter.ResetForTests();
    }
  }

  [Fact]
  public void Wire_InstallBackendFails_StaysNotReadyAndNeverRunsEagerTier() {
    bool eagerTierRan = false;
    List<string> log = new List<string>();

    try {
      WireContext context = new WireContext {
        InstallBackend = () => false,
        Log = log.Add,
        ApplyEagerTier = () => eagerTierRan = true
      };

      RimWorldAdapter.Wire(context);

      Assert.False(RimWorldAdapter.Ready);
      Assert.False(eagerTierRan);
    } finally {
      RimWorldAdapter.ResetForTests();
    }
  }

  [Fact]
  public void Wire_EagerTierThrows_StaysNotReady() {
    List<string> log = new List<string>();

    try {
      WireContext context = new WireContext {
        InstallBackend = () => true,
        Log = log.Add,
        ApplyEagerTier = () => throw new InvalidProgramException("bad il")
      };

      Assert.Throws<InvalidProgramException>(() => RimWorldAdapter.Wire(context));

      Assert.False(RimWorldAdapter.Ready);
    } finally {
      RimWorldAdapter.ResetForTests();
    }
  }

  [Fact]
  public void Wire_CalledAgainAfterAnEarlierWire_IsANoOpThatKeepsTheBookkeeping() {
    List<string> log = new List<string>();
    int installBackendCalls = 0;
    int eagerTierCalls = 0;

    try {
      WireContext context = new WireContext {
        InstallBackend = () => {
          installBackendCalls++;
          return true;
        },
        Log = log.Add,
        ApplyEagerTier = () => eagerTierCalls++
      };

      RimWorldAdapter.Wire(context);
      RimWorldAdapter.Wire(context);

      Assert.Equal(1, installBackendCalls);
      Assert.Equal(1, eagerTierCalls);
      Assert.True(RimWorldAdapter.Ready);
    } finally {
      RimWorldAdapter.ResetForTests();
    }
  }
}
