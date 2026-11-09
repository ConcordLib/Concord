using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Backend;
using Concord.Emit;
using Xunit;

namespace Concord.RimWorld.Tests;

public class DispatchTests {
  [Fact]
  public void ApplyComposed_RecordsTheOwnerAndReturnsAHandleThatDisposesCleanly() {
    FakeBackend backend = new FakeBackend();
    MethodBase target = TargetMethod();
    IReadOnlyList<Injection> added = new List<Injection> { MakeInjection(target) };

    IBackendHandle handle = backend.ApplyComposed(target, added);

    Assert.Equal(1, backend.ApplyComposedCallCount);
    Assert.Equal(1, backend.PatchedTargetCount);
    Assert.Equal(new[] { "test-owner" }, backend.OwnersOf(target));

    handle.Dispose();

    Assert.False(handle.IsApplied);
  }

  private static MethodBase TargetMethod() {
    return typeof(DispatchTests).GetMethod(
        nameof(StaticTarget),
        BindingFlags.NonPublic | BindingFlags.Static);
  }

  private static Injection MakeInjection(MethodBase method) {
    return new Injection(method, new InjectAt.Head(), "test-owner", 0);
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static void StaticTarget() {
  }
}

internal sealed class FakeBackend : IPatchBackend {
  private readonly Dictionary<MethodBase, List<string>> owners = new Dictionary<MethodBase, List<string>>();

  public int ApplyComposedCallCount;

  public string Name => nameof(FakeBackend);

  public int PatchedTargetCount => owners.Count;

  public TargetVerdict Judge(MethodBase target) {
    return TargetVerdict.Accept();
  }

  public bool OwnsBody(MethodBase target) {
    return false;
  }

  public bool Recompose(MethodBase target) {
    return false;
  }

  public IReadOnlyList<string> OwnersOf(MethodBase target) {
    return owners.TryGetValue(target, out List<string> list) ? list : Array.Empty<string>();
  }

  public IBackendHandle ApplyComposed(MethodBase target, IReadOnlyList<Injection> added) {
    ApplyComposedCallCount++;

    if (!owners.TryGetValue(target, out List<string> list)) {
      list = new List<string>();
      owners[target] = list;
    }

    foreach (Injection injection in added) {
      list.Add(injection.Owner);
    }

    return new FakeHandle { Original = target };
  }
}

internal sealed class FakeHandle : IBackendHandle {
  public MethodBase Original { get; set; }
  public bool IsApplied { get; set; } = true;
  public int DisposeCallCount;

  public void Dispose() {
    DisposeCallCount++;
    IsApplied = false;
  }
}
