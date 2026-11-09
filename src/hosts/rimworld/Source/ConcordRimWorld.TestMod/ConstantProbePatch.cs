namespace Concord.RimWorld.TestMod;

[Patch(typeof(TestMod))]
public abstract class ConstantProbePatch {
  [Inject(nameof(TestMod.Threshold), 18f, At.Constant)]
  private static float RaiseThreshold(float original) {
    return 20f;
  }
}
