using System.Collections.Generic;
using Verse;

namespace Concord.RimWorld.SurfaceTest;

// DoMainMenuControls runs every frame the main menu is up, so every probe reports once and the
// rest of its firings are dropped. The run script waits on the "done" line, which only appears
// after all Expected keys have landed - a missing probe therefore times the run out instead of
// silently passing.
internal static class Probe {
    private const int Expected = 7;

    private static readonly HashSet<string> Reported = new HashSet<string>();
    private static bool announced;

    internal static void Report(string key, string detail) {
        if (!Reported.Add(key)) {
            return;
        }

        Log.Message("[SURF] " + key + " " + detail);

        if (Reported.Count >= Expected && !announced) {
            announced = true;
            Log.Message("[SURF] done reported=" + Reported.Count);
        }
    }

    internal static void Fail(string key, string detail) {
        Log.Message("[SURF] FAILED " + key + " " + detail);
    }
}
