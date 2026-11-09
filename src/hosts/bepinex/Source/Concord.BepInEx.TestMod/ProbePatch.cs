using System.Runtime.CompilerServices;
using Concord;

namespace Concord.BepInEx.TestMod;

public class ProbeTargets {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Clean() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Contested() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Late() {
        return 1;
    }
}

[Patch]
public abstract class ProbePatch : ProbeTargets {
    [Inject(At.Head, nameof(Clean))]
    public static void OnClean(ControlHandle<int> ch) {
        TestPlugin.Mark("probe-clean");
    }

    [Inject(At.Head, nameof(Contested))]
    public static void OnContested(ControlHandle<int> ch) {
        TestPlugin.Mark("probe-contested");
    }

    [Inject(At.Head, nameof(Late))]
    public static void OnLate(ControlHandle<int> ch) {
        TestPlugin.Mark("probe-late");
    }
}
