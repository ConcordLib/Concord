using System;
using System.Collections.Generic;
using System.Reflection;
using Concord.Backend;
using Concord.Emit;

namespace Concord.BepInEx.Tests;

internal sealed class FakePatchBackend : IPatchBackend {
    public string Name => "fake";

    public int PatchedTargetCount { get; private set; }

    public TargetVerdict Judge(MethodBase target) {
        return TargetVerdict.Accept();
    }

    public IBackendHandle ApplyComposed(MethodBase target, IReadOnlyList<Injection> added) {
        PatchedTargetCount++;
        return new FakeHandle { Original = target };
    }

    public IReadOnlyList<string> OwnersOf(MethodBase target) {
        return Array.Empty<string>();
    }

    public bool OwnsBody(MethodBase target) {
        return false;
    }

    public bool Recompose(MethodBase target) {
        return false;
    }
}

internal sealed class FakeHandle : IBackendHandle {
    public MethodBase Original { get; set; }

    public bool IsApplied { get; private set; } = true;

    public void Dispose() {
        IsApplied = false;
    }
}
