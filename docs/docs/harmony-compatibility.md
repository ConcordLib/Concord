# Harmony compatibility

RimWorld's Concord mod can run alongside Harmony patches on the same method. This page explains when that kicks in, what you have to configure (nothing), and what happens when a patch shape can't be composed.

This feature is specific to the RimWorld adapter. Concord Core has no Harmony awareness at all.

## The problem this solves

Say two mods both want to change `Pawn.TakeDamage`. One mod patches it with Harmony. Another patches it with Concord.

Harmony and Concord each work by installing a detour: they redirect the method to a wrapper that runs their code, then (usually) calls the original. If both mods redirect the same method, only one detour wins. Whichever library installed second either overwrites the other's redirect or gets overwritten by it, and one mod's change silently stops running. Nobody gets an error. The method just doesn't do what one of the mods expects anymore.

This is not a hypothetical. Most RimWorld mods use Harmony, and a modder adopting Concord for a new mod will often end up patching a method that some other, unrelated mod has already patched with Harmony.

## What Concord does about it

Concord checks each method it's about to patch. If nothing else has touched that method, Concord installs its normal detour and moves on. That path is unchanged and has no extra cost.

If Harmony already has a patch on that method, or gets one later, Concord treats the method as **contested**. Instead of installing its own detour, Concord asks Harmony for a single transpiler slot on that method, at the lowest priority Harmony supports. Every time Harmony rebuilds that method, whether because your mod's Concord patches changed or because some other mod added or removed a Harmony patch, Concord's transpiler runs again and re-applies its own injections onto whatever instruction stream Harmony hands it at that point.

The result: both mods' changes run on the same method. Harmony's prefixes and postfixes do what they always do, and Concord's injections get woven into the method body Harmony produces, instead of getting bulldozed by it.

Here's the short version:

| Situation | What Concord does |
| --- | --- |
| No Harmony patch on the method | Installs its normal fast detour |
| Harmony already patches the method | Registers one low-priority transpiler and recomposes on every Harmony rebuild |
| Harmony patches the method later | Detects it and reports the conflict; that method's Concord injections stop running until it's resolved |

That last row matters: coexistence only works when Concord knows about the Harmony patch *before* it installs its own detour. If Concord's raw detour is already in place and Harmony patches the same method afterward, Concord doesn't retroactively convert to the bridge path. It reports the conflict loudly instead, through a periodic watchdog check, so you find out rather than silently losing your patch.

## Nothing to configure

You don't opt in to any of this. It's automatic.

The bridge itself lives in a separate, small DLL that ships next to Concord but sits outside the folder RimWorld scans for mod assemblies. Concord loads it itself, and only after confirming a supported Harmony is actually present in the game. If Harmony isn't loaded, that DLL never loads either, and nothing about your mod's behavior changes.

Load order between your Harmony mod and your Concord mod doesn't matter. Harmony can patch the method first and Concord can show up later, or the other way around. Either order gets detected and routed the same way.

Most Concord patches also wait to apply until after every mod's constructor and static constructor has finished running, so by the time Concord decides whether a method is contested, it's looking at the fullest possible picture of what every other mod's Harmony patches have already done. A small set of patches that Concord itself needs during mod loading can't wait that long and apply immediately instead, but that's Concord's own load-machinery, not something a mod author needs to think about.

## When it can't compose cleanly

A few patch shapes can't be safely woven into a Harmony transpiler stream, and Concord rejects them rather than risk corrupting the method:

- **Constructor `Around` injections.** A whole-method `Around` on a constructor needs the object fully constructed before it can safely wrap the call, and Harmony's constructor patching doesn't guarantee that.
- **Methods with Harmony 2.4 inner patches.** Inner prefixes and postfixes (patches that target code *inside* another patch's replacement) aren't something Concord's composition step can reason about.
- **Async and iterator methods, targeted at the wrong entry point.** Concord needs to work against the actual state-machine method, not the method you see in source. If the target it's handed isn't that canonical method, it backs off.
- **Shared reference-type generic instantiations.** Patching `Box<string>.Get` compiles to one shared method body for every reference-type `Box<T>`, so a wrapper built for one would silently apply to all of them. Concord rejects this at patch time. Value-type instantiations like `Box<int>.Get` are fine, since those get their own compiled body.
- **Injection code that calls `Assembly.GetExecutingAssembly()`.** Harmony rewrites that call to point at the target's assembly instead of yours, which breaks any injection that relies on it.

When Concord hits one of these, it falls back to its normal raw detour on that method and logs a warning explaining why, rather than trying to force a composition that could break the game.

There's also a separate case Concord can't catch at patch time: if some other mod adds a Harmony inner patch to a method *after* Concord already routed it through the bridge, and Concord itself never re-applies to that method again, there's no compose-time moment for Concord to notice. That gets caught later, if at all, by the same periodic watchdog check mentioned above, not immediately.

## Settings

Two toggles live in Concord's in-game mod settings, both under `ConcordSettings`:

| Setting | Default | What it does |
| --- | --- | --- |
| Bridge Routing Enabled | On | Kill switch. Turn it off and Concord never hands routing to Harmony, even on a contested method. Concord's raw-detour conflict reporting still works, since that path doesn't depend on the bridge. |
| Route Everything When Harmony Present | Off | Compat mode. Turn it on and Concord routes every method through the bridge whenever Harmony is loaded, not just methods that are actually contested. |

Both take effect on the next game launch, not immediately.

## Checking that it's working

The bridge writes a small set of log markers you can grep for in the RimWorld log if you want to confirm coexistence is actually happening:

| Marker | Meaning |
| --- | --- |
| `[Concord.Coex] bridge-active` | The bridge DLL loaded and found a supported Harmony. |
| `[Concord.Coex] routed-contested` | Concord routed a specific method through Harmony instead of its own detour. |
| `[Concord.Coex] flush-complete` | Concord's deferred patch queue finished applying after mod load. |
| `[Concord.Coex] late-contention` | A method Concord raw-detoured got a foreign Harmony patch afterward; that method's Concord injections aren't running. |
| `[Concord.Coex] stream-rejected` | Concord's transpiler couldn't safely convert Harmony's instruction stream for a method and left it untouched. |

If you never see `bridge-active`, either Harmony wasn't loaded or the bridge didn't detect a supported version. Concord validates against the Harmony 2.4.x line; that's the only line it's been checked against a real Harmony binary for.

## Where to go next

[How patches work](how-patches-work.md) covers the wrapper Concord builds when there's no contention. [Migrating from Harmony and Prepatcher](migration.md) is the place to start if you're moving an existing Harmony patch to Concord's own API rather than running both side by side. [Troubleshooting](troubleshooting.md) lists the `CONCxxx` diagnostic codes you might see if a patch fails to apply.
