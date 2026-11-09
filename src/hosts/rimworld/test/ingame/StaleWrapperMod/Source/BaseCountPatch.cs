using Concord;
using Verse;

namespace Concord.RimWorld.StaleTest;

public static class Probe {
    public static int Counted;

    public static int Bump(int count) {
        return count + 1;
    }
}

[Patch(typeof(IngredientCount))]
public static class BaseCountPatch {
    [Inject(At.Head, nameof(IngredientCount.GetBaseCount))]
    public static unsafe void OnGetBaseCount(ControlHandle<float> ch) {
        delegate*<int, int> bump = &Probe.Bump;
        Probe.Counted = bump(Probe.Counted);
    }
}
