using Concord;
using Verse;

namespace Concord.RimWorld;

[Patch]
public abstract class GameComponentExposePatch : GameComponent {
  [Inject(At.Return, nameof(ExposeData))]
  public void ScribeAttachedProperties(ControlHandle ch) {
    SaveHooks.ScribeAttached(this);
  }
}
