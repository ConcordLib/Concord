using Concord;
using RimWorld.Planet;

namespace Concord.RimWorld;

[Patch]
public abstract class WorldComponentExposePatch : WorldComponent {
  protected WorldComponentExposePatch(World world) : base(world) {
  }

  [Inject(At.Return, nameof(ExposeData))]
  public void ScribeAttachedProperties(ControlHandle ch) {
    SaveHooks.ScribeAttached(this);
  }
}
