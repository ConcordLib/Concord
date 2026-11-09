using Concord;
using Verse;

namespace Concord.RimWorld;

[Patch]
public abstract class ThingCompExposePatch : ThingComp {
  [Inject(At.Return, nameof(PostExposeData))]
  public void ScribeAttachedProperties(ControlHandle ch) {
    SaveHooks.ScribeAttached(this);
  }
}
