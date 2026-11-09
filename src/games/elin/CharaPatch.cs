using System;
using Concord;

namespace Concord.Elin;

[Patch]
public abstract class CharaPatch : Chara {
    [Inject(At.Return, nameof(Duplicate), parameterTypes: new Type[0])]
    public void CopyAttached(ControlHandle<Chara> ch) {
        SaveHooks.CopyCard(this, ch.ReturnValue);
    }
}
