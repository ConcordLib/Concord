using Concord;
using RimWorld.Planet;

namespace Concord.RimWorld;

[Patch]
public abstract class WorldObjectExposePatch : WorldObject {
  [Inject(At.Return, nameof(ExposeData))]
  public void ScribeAttachedProperties(ControlHandle ch) {
    SaveHooks.ScribeAttached(this);
  }
}
