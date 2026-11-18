using System.Runtime.CompilerServices;

namespace Concord.Orchestration.Tests.ManifestAssembly;

public class ParameterTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public string TwoLocals(int seed) {
        int doubled = seed * 2;
        int tripled = seed * 3;
        return string.Concat(doubled.ToString(), tripled.ToString());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Priced(int listed) {
        return ParameterRules.Apply(listed + 1, 2f);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Checkout(int seed) {
        int outside = ParameterRules.Total(seed);
        ParameterSliceFrom.Begin();
        int inside = ParameterRules.Total(outside);
        ParameterSliceTo.End();
        return ParameterRules.Total(inside);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Plain(int x) {
        return x * 2;
    }
}

public static class ParameterRules {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Apply(int basePrice, float rate) {
        return (int)(basePrice * rate);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Total(int seed) {
        return seed + 1;
    }
}

public static class ParameterSliceFrom {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Begin() { }
}

public static class ParameterSliceTo {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void End() { }
}

public static class ParameterRecorder {
    public static int Indexed;

    public static int Named;

    public static int SeenBase;

    public static float SeenRate;

    public static int Section;

    public static int SliceHits;

    public static int SliceSeed;
}

public static class ParameterManifestRuns {
    public static int Count;
}

public abstract class LocalParameterPatch : ParameterTarget {
    private void ReadByIndex(int value) {
        ParameterRecorder.Indexed = value;
    }

    private void ReadByName(int value) {
        ParameterRecorder.Named = value;
    }
}

public abstract class CaptureParameterPatch : ParameterTarget {
    private void AfterApply(int basePrice, float rate) {
        ParameterRecorder.SeenBase = basePrice;
        ParameterRecorder.SeenRate = rate;
    }
}

public abstract class BoundParameterPatch : ParameterTarget {
    private void OnPlain(int sectionId) {
        ParameterRecorder.Section = sectionId;
    }
}

public abstract class SliceParameterPatch : ParameterTarget {
    private void BeforeTotal(int seed) {
        ParameterRecorder.SliceHits++;
        ParameterRecorder.SliceSeed = seed;
    }
}

public abstract class HeadSliceParameterPatch : ParameterTarget {
    private void SlicedAtHead() { }
}
