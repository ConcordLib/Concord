using Concord;

namespace Concord.RimWorld.AttachedTest;

[Patch]
public abstract class ProbeThingPatch : ProbeThing {
    [Attached]
    public int pokes;

    [Inject(At.Tail, nameof(Poke))]
    public void AfterPoke(ControlHandle<int> ch) {
        pokes += 1;
        ch.ReturnValue = pokes;
    }
}
