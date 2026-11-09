using System.Collections;
using BepInEx;
using BepInEx.Logging;
using Concord;
using UnityEngine;

namespace Concord.Elin.TestMod;

[BepInPlugin("concordlib.concord.elin.testmod", "Concord Elin TestMod", "0.0.0")]
public sealed class TestPlugin : BaseUnityPlugin {
    private static ManualLogSource log;
    private static SavedField<Chara, int> visits;
    private static SavedField<Chara, VisitNote> note;
    private static SavedField<Zone, ZoneMood> mood;
    private static SavedField<Thing, int> thingTag;

    public static void Mark(string message) {
        log.LogInfo("[Concord.Elin.TestMod] " + message);
    }

    private void Awake() {
        log = Logger;
        visits = SavedField<Chara, int>.Register("visits", typeof(TestPlugin));
        note = SavedField<Chara, VisitNote>.Register("note", typeof(TestPlugin));
        mood = SavedField<Zone, ZoneMood>.Register("mood", typeof(TestPlugin));
        thingTag = SavedField<Thing, int>.Register("tag", typeof(TestPlugin));
        Patcher.Apply(typeof(TestPlugin).Assembly);
        BaseModManager.SubscribeEvent<Chara>("elin.chara_created", OnCharaCreated);
        StartCoroutine(Loop());
    }

    private static void OnCharaCreated(Chara chara) {
        if (chara == null || !visits.TryGet(chara, out int restoredVisits) || restoredVisits == 0) {
            return;
        }

        VisitNote restored = note.Get(chara);
        Mark("pc restored visits=" + restoredVisits + " note=" + (restored == null ? "null" : restored.Text));
    }

    private IEnumerator Loop() {
        while (true) {
            yield return new WaitForSeconds(5f);
            if (!EClass.core.IsGameStarted || EClass.pc == null) {
                continue;
            }

            int count = visits.Get(EClass.pc) + 1;
            visits.Set(EClass.pc, count);
            note.Set(EClass.pc, new VisitNote { Text = "visit " + count });
            mood.Set(EClass._zone, count % 2 == 0 ? ZoneMood.Busy : ZoneMood.Quiet);

            if (EClass.pc.things.Count > 0) {
                thingTag.Set(EClass.pc.things[0], count);
            }

            Mark("pc visits=" + count + " zone mood=" + mood.Get(EClass._zone));
        }
    }
}
