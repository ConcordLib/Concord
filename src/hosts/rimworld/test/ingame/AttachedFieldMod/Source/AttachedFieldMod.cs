using System;
using System.IO;
using Concord;
using Verse;

namespace Concord.RimWorld.AttachedTest;

public class AttachedFieldMod : Mod {
    public AttachedFieldMod(ModContentPack content) : base(content) {
        Patcher.Apply(typeof(AttachedFieldMod).Assembly);
        Log.Message("[ATTACH] applied");
        LongEventHandler.ExecuteWhenFinished(Run);
    }

    private static void Run() {
        try {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("ConcordProbeThing");
            Log.Message("[ATTACH] def found=" + (def != null) + " class=" + (def == null ? "-" : (def.thingClass == null ? "null" : def.thingClass.FullName)));
            if (def == null) {
                Log.Message("[ATTACH] FAILED def missing, thingdef count=" + DefDatabase<ThingDef>.DefCount);
                return;
            }

            ProbeThing first = Make(def, 1);
            ProbeThing second = Make(def, 2);

            first.Poke();
            first.Poke();
            int firstValue = first.Poke();
            int secondValue = second.Poke();

            Log.Message("[ATTACH] lowered first=" + firstValue + " second=" + secondValue);

            string path = Path.Combine(GenFilePaths.SaveDataFolderPath, "concord-attached-probe.xml");
            Scribe.saver.InitSaving(path, "concordprobe");
            Scribe_Deep.Look(ref first, "thing");
            Scribe.saver.FinalizeSaving();

            string written = File.ReadAllText(path);
            Log.Message("[ATTACH] saved label-present=" + written.Contains(".AttachedFieldProbe.pokes") + " body=" + Body(written));

            ProbeThing loaded = null;
            Scribe.loader.InitLoading(path);
            Scribe_Deep.Look(ref loaded, "thing");
            Scribe.loader.FinalizeLoading();

            if (loaded == null) {
                Log.Message("[ATTACH] FAILED load returned null");
                return;
            }

            int afterLoad = loaded.Poke();
            Log.Message("[ATTACH] roundtrip after-load=" + afterLoad);
            Log.Message("[ATTACH] done");
        } catch (Exception e) {
            Log.Message("[ATTACH] FAILED " + e);
        }
    }

    // ThingMaker.MakeThing runs Thing.PostMake, which reaches Find.UniqueIDsManager. There is no
    // game at the main menu, so build the instance directly and hand it an id.
    private static ProbeThing Make(ThingDef def, int id) {
        ProbeThing thing = new ProbeThing();
        thing.def = def;
        thing.thingIDNumber = id;
        return thing;
    }

    private static string Body(string xml) {
        int start = xml.IndexOf("<thing>", StringComparison.Ordinal);
        if (start < 0) {
            return xml.Replace("\n", " ");
        }

        return xml.Substring(start).Replace("\n", " ");
    }
}
