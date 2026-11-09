using Concord;

namespace Concord.Elin.TestMod;

public enum ZoneMood {
    Quiet,
    Busy,
}

public sealed class VisitNote {
    public string Text;
}

[Patch]
public abstract class CharaTickPatch : Chara {
    [Attached]
    public int ticks;

    [Inject(At.Head, nameof(Tick))]
    public void CountTick(ControlHandle ch) {
        if (IsPC) {
            ticks++;
            if (ticks % 100 == 0) {
                TestPlugin.Mark("pc ticks=" + ticks);
            }
        }
    }
}
