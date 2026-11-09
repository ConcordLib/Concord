using Concord;
using Verse;

namespace Concord.RimWorld;

[Patch]
public abstract class ThingExposePatch : Thing {
  [Inject(At.Return, nameof(ExposeData))]
  public void ScribeAttachedProperties(ControlHandle ch) {
    SaveHooks.ScribeAttached(this);
  }
}
