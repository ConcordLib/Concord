using Verse;

namespace Concord.RimWorld.TestMod;

public class TestMod : Mod {
  public TestMod(ModContentPack content) : base(content) {
    Concord.Patcher.Apply(typeof(TestMod).Assembly);
    Log.Message("[Concord.RimWorld.TestMod] test mod patches applied");
  }

  [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S3400", Justification = "Must be a method so Concord can patch it; the test probes the patched return value.")]
  public static float Threshold() {
    return 18f;
  }
}
