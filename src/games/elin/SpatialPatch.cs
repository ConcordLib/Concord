using System.Runtime.Serialization;
using Concord;

namespace Concord.Elin;

[Patch]
public abstract class SpatialPatch : Spatial {
    [Inject(At.Head, "OnSerializing")]
    public void WriteAttached(ControlHandle ch, StreamingContext context) {
        SaveHooks.WriteSpatial(this);
    }

    [Inject(At.Head, "OnDeserialized")]
    public void ReadAttached(ControlHandle ch, StreamingContext context) {
        SaveHooks.ReadSpatial(this);
    }
}
