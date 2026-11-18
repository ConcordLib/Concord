using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Concord;

namespace Concord.MonoMod.Tests.ManifestAssembly;

public class MonoTarget {
    private int fuel = 10;

    public int FuelSnapshot => fuel;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Bump() {
        MonoRecorder.Ran.Add("body");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Fuel(int delta) {
        return -1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Priced(int listed) {
        return MonoRules.Apply(listed + 1, 2f);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Ordered() {
        MonoRecorder.Ran.Add("body");
    }
}

public static class MonoRules {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Apply(int basePrice, float rate) {
        return (int)(basePrice * rate);
    }
}

public static class MonoRecorder {
    public static readonly List<string> Ran = [];

    public static int SeenBase;

    public static float SeenRate;
}

public abstract class HeadPatch : MonoTarget {
    private void BumpHead() {
        MonoRecorder.Ran.Add("head");
    }
}

public abstract class FieldPatch : MonoTarget {
    private int injectedFuel;

    private void OnFuel(ControlHandle<int> ch, int delta) {
        injectedFuel += delta;
        ch.ReturnValue = injectedFuel;
        ch.Cancel();
    }
}

public abstract class CapturePatch : MonoTarget {
    private void AfterApply(int basePrice, float rate) {
        MonoRecorder.SeenBase = basePrice;
        MonoRecorder.SeenRate = rate;
    }
}

public abstract class AOrderedEarlyPatch : MonoTarget {
    private void OnOrdered() {
        MonoRecorder.Ran.Add("early");
    }
}

public abstract class ZOrderedLatePatch : MonoTarget {
    private void OnOrdered() {
        MonoRecorder.Ran.Add("late");
    }
}
