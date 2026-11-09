using Concord;
using RimWorld;

namespace Concord.RimWorld;

[Patch]
public abstract class FactionExposePatch : Faction {
  [Inject(At.Return, nameof(ExposeData))]
  public void ScribeAttachedProperties(ControlHandle ch) {
    SaveHooks.ScribeAttached(this);
  }
}
