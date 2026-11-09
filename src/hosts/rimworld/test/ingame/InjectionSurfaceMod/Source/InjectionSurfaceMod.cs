using Concord;
using Verse;

namespace Concord.RimWorld.SurfaceTest;

public class InjectionSurfaceMod : Mod {
    public InjectionSurfaceMod(ModContentPack content) : base(content) {
        Patcher.Apply(typeof(InjectionSurfaceMod).Assembly);
        Log.Message("[SURF] applied");
    }
}
