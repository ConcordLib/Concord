using System.Runtime.Serialization;
using Concord;

namespace Concord.Elin;

[Patch]
public abstract class CardPatch : Card {
    [Inject(At.Head, "_OnSerializing")]
    public void WriteAttached(ControlHandle ch, StreamingContext context) {
        SaveHooks.WriteCard(this);
    }

    [Inject(At.Head, "_OnDeserialized")]
    public void ReadAttached(ControlHandle ch, StreamingContext context) {
        SaveHooks.ReadCard(this);
    }

    [Inject(At.Return, nameof(Duplicate))]
    public void CopyAttached(ControlHandle<Thing> ch, int num) {
        SaveHooks.CopyCard(this, ch.ReturnValue);
    }
}
