using Concord;
using Verse;

namespace Concord.RimWorld;

[Patch]
public abstract class HediffExposePatch : Hediff {
  [Inject(At.Return, nameof(ExposeData))]
  public void ScribeAttachedProperties(ControlHandle ch) {
    SaveHooks.ScribeAttached(this);
  }
}
