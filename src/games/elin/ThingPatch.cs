using Concord;

namespace Concord.Elin;

[Patch]
public abstract class ThingPatch : Thing {
    [Inject(At.Head, nameof(CanStackTo))]
    public void SplitOnAttached(ControlHandle<bool> ch, Thing to) {
        if (!SaveHooks.CardsEqual(this, to)) {
            ch.ReturnValue = false;
            ch.Cancel();
        }
    }
}
