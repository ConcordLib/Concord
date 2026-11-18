using System.Runtime.CompilerServices;

namespace Concord.Orchestration.Tests.ManifestAssembly;

public class ManifestTarget {
    public int Count;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Bump() {
        Count++;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Ordered() {
        Count++;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Debugged() {
        Count++;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Overridden() {
        Count++;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Overloaded() {
        Count++;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Build() {
        ManifestSliceFrom.Start();
        GC.KeepAlive(new ManifestBuilt());
        ManifestSliceTo.End();
    }
}

public sealed class ManifestBuilt { }

public static class ManifestSliceFrom {
    public static void Start() { }
}

public static class ManifestSliceTo {
    public static void End() { }
}

public abstract class HeadPatch : ManifestTarget {
    private void BumpHead() { }
}

public abstract class FirstOrderedPatch : ManifestTarget {
    private void OnOrdered() { }
}

public abstract class SecondOrderedPatch : ManifestTarget {
    private void OnOrdered() { }
}

public abstract class DebugPatch : ManifestTarget {
    private void OnDebugged() { }
}

public abstract class BuildPatch : ManifestTarget {
    private void OnBuilt() { }
}

public abstract class AttachedPatch : ManifestTarget {
    public int Visits;
}

public abstract unsafe class OverloadPatch : ManifestTarget {
    private void On() { }

    private void On(int value) { }

    private void On(ref int value) { }

    private void On(int* value) { }

    private void On<T>(T value) { }

    private void On(List<string> values) { }

    private void On(int[,] grid) { }

    private void On(Hidden hidden) { }

    private sealed class Hidden { }
}

[Patch]
public abstract class OverriddenPatch : ManifestTarget {
    [Inject(At.Head, nameof(Bump))]
    private void OnOverridden() { }
}

[Patch]
public abstract class UnlistedPatch : ManifestTarget {
    [Inject(At.Head, nameof(Bump))]
    private void BumpHead() { }
}
