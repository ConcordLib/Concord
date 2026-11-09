using System.Collections.Generic;
using Concord;
using RimWorld;
using UnityEngine;
using Verse;

namespace Concord.RimWorld.SurfaceTest;

// Probes the state slot, [Capture], [InjectNew] and [Slice] against a real RimWorld body rather
// than a hand-written fixture. Every expectation below is a constant read off the decompiled
// MainMenuDrawer.DoMainMenuControls, so a wrong answer is a wrong answer and not a judgement call:
//
//   BeginGroup(rect)   <- occurrence 1. UnityEngine.GUI below 1.3, Verse.Widgets from 1.3 on
//   OptionListingUtility.DrawOptionListing(rect2, list)  <- occurrence 1, rect2.width == 170
//   newobj ListableOption_WebLink(string, string, Texture2D) x7, naming a|b where b is the 1.6 and 1.7 name
//       FictionPrimer, LudeonBlog, Forums|Subreddit, OfficialWiki, TynansTwitter|TynansX, TynansDesignBook, HelpTranslate
//   newobj ListableOption_WebLink(string, Action, Texture2D) x1   (BuySoundtrack, excluded by signature)
//   OptionListingUtility.DrawOptionListing(webRect, list2) <- occurrence 2, webRect.width == 145, list2.Count == 8
//   BeginGroup(webRect)                                  <- BeginGroup occurrence 2
//   Widgets.ButtonText(language button)                  <- occurrence 1, and the slice's closing anchor
//   EndGroup() x2

// A head write and a tail read of one declaration's state slot. rect comes from the caller's
// resolution, so the value is not knowable at the tail's compile time - only the slot can carry it.
[Patch(typeof(MainMenuDrawer))]
public static class MainMenuStatePatch {
    [Inject(At.Head, nameof(MainMenuDrawer.DoMainMenuControls))]
    private static void Enter(ControlHandle ch, Rect rect) {
        ch.SetState(rect.width);
        Probe.Report("state-head", "wrote=" + rect.width.ToString("0.000"));
    }

    [Inject(At.Tail, nameof(MainMenuDrawer.DoMainMenuControls))]
    private static void Leave(ControlHandle ch) {
        Probe.Report("state-tail", "read=" + ch.GetState<float>().ToString("0.000"));
    }
}

// An unsliced invoke injection binding both arguments of the matched call. by:1 counts across the
// whole body, so this is the rect2 listing: width 170.
[Patch(typeof(MainMenuDrawer))]
public static class MainMenuCapturePatch {
    [Inject(
        nameof(MainMenuDrawer.DoMainMenuControls),
        typeof(OptionListingUtility),
        nameof(OptionListingUtility.DrawOptionListing),
        At.Head,
        1)]
    private static void OnFirstListing([Capture(1)] Rect rect, [Capture(2)] List<ListableOption> options) {
        Probe.Report("capture", "width=" + rect.width.ToString("0") + " count=" + options.Count);
    }
}

// The same injection shape as MainMenuCapturePatch with only [Slice] added, so the two probes can
// only disagree if the slice actually moved which site by:1 resolves to. The open anchor is
// exclusive, so occurrence 1 of DrawOptionListing inside the range is the body's occurrence 2:
// the webRect listing, width 145 over the 8-entry list2.
//
// list2[0] is the FictionPrimer weblink, whose label MainMenuArgumentPatch rewrites at its newobj.
// Reading it back here is what proves the rewritten value reached the constructor rather than just
// that the injection ran.
[Patch(typeof(MainMenuDrawer))]
public static class MainMenuSlicePatch {
    [Inject(
        nameof(MainMenuDrawer.DoMainMenuControls),
        typeof(OptionListingUtility),
        nameof(OptionListingUtility.DrawOptionListing),
        At.Head,
        1)]
    [Slice(
        typeof(OptionListingUtility),
        nameof(OptionListingUtility.DrawOptionListing),
        1,
        typeof(Widgets),
        nameof(Widgets.ButtonText),
        1)]
    private static void OnFencedListing([Capture(1)] Rect rect, [Capture(2)] List<ListableOption> options) {
        Probe.Report("slice", "width=" + rect.width.ToString("0") + " count=" + options.Count);
        Probe.Report("argument-effect", "label0=" + (options.Count > 0 ? options[0].label : "<empty>"));
    }
}

// A construction-site injection binding a constructor argument. Occurrence 3 of the 3-arg weblink
// ctor is the Forums link below 1.6 and the Subreddit link from 1.6, a url literal either way.
[Patch(typeof(MainMenuDrawer))]
public static class MainMenuNewObjPatch {
    [InjectNew(
        nameof(MainMenuDrawer.DoMainMenuControls),
        typeof(ListableOption_WebLink),
        At.Head,
        3,
        invokeParameterTypes: new[] { typeof(string), typeof(string), typeof(Texture2D) })]
    private static void OnThirdWebLink([Capture(2)] string url) {
        Probe.Report("injectnew", "url=" + url);
    }
}

// A construction-site argument rewrite. Occurrence 1 of the 3-arg weblink ctor is FictionPrimer;
// prefixing its label is visible on the main menu and observable through MainMenuSlicePatch.
[Patch(typeof(MainMenuDrawer))]
public static class MainMenuArgumentPatch {
    [InjectNew(
        nameof(MainMenuDrawer.DoMainMenuControls),
        typeof(ListableOption_WebLink),
        At.Argument,
        1,
        invokeParameterTypes: new[] { typeof(string), typeof(string), typeof(Texture2D) },
        arg: 1)]
    private static string RelabelFirstWebLink(string original) {
        Probe.Report("argument", "original=" + original);
        return "Concord|" + original;
    }
}
