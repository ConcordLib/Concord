using Concord;
using Verse;

namespace Concord.RimWorld;

[Patch]
public abstract class MapComponentExposePatch : MapComponent {
  protected MapComponentExposePatch(Map map) : base(map) {
  }

  [Inject(At.Return, nameof(ExposeData))]
  public void ScribeAttachedProperties(ControlHandle ch) {
    SaveHooks.ScribeAttached(this);
  }
}
