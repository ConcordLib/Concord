using Concord;
using Verse;

namespace Concord.RimWorld.TestMod;

[Patch]
public abstract class TestPatch : Verse.Root {
  private static bool logged;

  [Inject(At.Head, nameof(Verse.Root.Update))]
  [System.Diagnostics.CodeAnalysis.SuppressMessage("Critical Code Smell", "S2696", Justification = "This injected instance patch deliberately sets a static once-flag so the probe logs only on the first tick.")]
  public void ProbeUpdate(ControlHandle ch) {
    if (logged) {
      return;
    }

    logged = true;
    Log.Message("[Concord.RimWorld.TestMod] probe patch fired on Root.Update");
    Log.Message("[Concord.RimWorld.TestMod] constant probe: Threshold() = " + TestMod.Threshold());
  }
}
