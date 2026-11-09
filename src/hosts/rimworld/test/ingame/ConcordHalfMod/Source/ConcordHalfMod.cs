using Concord;
using Verse;

namespace Concord.RimWorld.CoexTest;

[Patch(typeof(SharedTarget))]
public static class SharedTargetPatch {
    [Inject(At.Head, nameof(SharedTarget.Compute))]
    public static void OnCompute(ControlHandle<int> ch) {
        SharedTarget.HeadCount++;
        Log.Message("[COEX] concord-head");
    }
}

public class ConcordHalfMod : Mod {
    public ConcordHalfMod(ModContentPack content) : base(content) {
        Patcher.Apply(typeof(ConcordHalfMod).Assembly);
        LongEventHandler.ExecuteWhenFinished(() => LongEventHandler.ExecuteWhenFinished(() => {
            int result = SharedTarget.Compute(41);
            Log.Message("[COEX] invoke-result=" + result + " head-count=" + SharedTarget.HeadCount);
        }));
    }
}
