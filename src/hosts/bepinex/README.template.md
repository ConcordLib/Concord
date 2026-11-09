Concord is a runtime patching library. This plugin loads it into any BepInEx 6 mono game so mods can write [Patch]/[Inject] templates instead of Harmony prefixes and postfixes.

## For players

Unzip into `BepInEx/plugins`. You get `BepInEx/plugins/Concord/` with three files inside. There is nothing to configure.

Mods that depend on Concord will not work without it.

## For mod authors

- Author patches as [Patch] classes that extend the target type, with [Inject] methods at Head, Tail, or Around positions
- Root plugins: declare `[BepInDependency("concordlib.concord")]` so you load after Concord. A plugin that sorts before it by GUID patches before the router exists
- Game packages loaded by a second chainloader (Elin, for one) cannot use BepInDependency on a root plugin. Check for the loaded `Concord.BepInEx` assembly instead
- Saved per-instance state needs a game package on top. Concord.Elin is the first one

### HarmonyX coexistence

Concord and HarmonyX share methods the same way Concord and Harmony do on RimWorld. Concord bundles every injection on a method into one transpiler before HarmonyX sees it. If a HarmonyX prefix returns false, the original never runs, and neither do your Concord injections. Concord rejects a few shapes up front: Around on a constructor, targets with IL manipulators, filter and fault exception regions, and a few rarer IL cases. Two settings in `BepInEx/config/concordlib.concord.cfg` change this: one disables the handoff, one sends every patched method through HarmonyX.

Docs: https://concordlib.dev
Source and issues: https://github.com/ConcordLib/BepInEx
