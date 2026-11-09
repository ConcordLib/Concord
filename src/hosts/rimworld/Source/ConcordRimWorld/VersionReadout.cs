using System;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace Concord.RimWorld;

/// <summary>
///     Draws Concord's versions in the main menu corner, under the readout RimWorld already shows.
/// </summary>
/// <remarks>
///     <para>
///         This patches <see cref="MainMenuDrawer.MainMenuOnGUI" />, the method that calls
///         <c>DrawInfoInCorner</c>, rather than the readout itself. Harmony's mod patches
///         <c>DrawInfoInCorner</c> to add its own label, and a Concord patch on that same method
///         displaces Harmony's. Staying one level up keeps both readouts.
///     </para>
///     <para>
///         Harmony draws its label at a hard-coded y that assumes the vanilla block is two lines tall.
///         Concord takes the line below Harmony's when Harmony is loaded, and Harmony's own slot when
///         it is not.
///     </para>
/// </remarks>
[Patch(typeof(MainMenuDrawer))]
public abstract class VersionReadout {
  private const string HarmonyLabelType = "HarmonyMod.VersionControl_DrawInfoInCorner_Patch";
  private const float LabelX = 10f;
  private const float HarmonyLabelY = 58f;

  // Harmony's label box is a full line tall but its glyphs do not fill it, so stepping a whole
  // line leaves a visible gap. Three quarters of a line closes about half of it.
  private const float LineStep = 0.75f;

  private static bool? harmonyPresent;

  /// <summary>Draws the Concord line after RimWorld has drawn the rest of the main menu.</summary>
  /// <param name="ch">The Concord control handle for the void target.</param>
  [Inject(At.Tail, nameof(MainMenuDrawer.MainMenuOnGUI))]
  public static void DrawConcordVersion(ControlHandle ch) {
    try {
      Text.Font = GameFont.Small;
      GUI.color = new Color(1f, 1f, 1f, 0.5f);

      Vector2 size = Text.CalcSize(VersionInfo.Line);
      float y = HarmonyPresent ? HarmonyLabelY + (size.y * LineStep) : HarmonyLabelY;
      Widgets.Label(new Rect(LabelX, y, size.x, size.y), VersionInfo.Line);

      GUI.color = Color.white;
    } catch (Exception) { // NOSONAR a draw failure must never take the main menu down with it
      GUI.color = Color.white;
    }
  }

  private static bool HarmonyPresent => harmonyPresent ??= ResolveHarmonyLabel();

  private static bool ResolveHarmonyLabel() {
    return Array.Exists(AppDomain.CurrentDomain.GetAssemblies(), assembly => assembly.GetType(HarmonyLabelType, false) != null);
  }
}
