# Harmony compatibility

Concord's RimWorld adapter can share a method with Harmony patches without extra setup. If Concord can't combine the patches, it logs a warning. It then falls back to its normal detour.

The RimWorld adapter provides this support. Concord Core doesn't know about Harmony.

## Why Concord needs a bridge

Two mods can target the same method: one mod may patch `Pawn.TakeDamage` with Harmony while another patches it with Concord.

Harmony and Concord both install a detour. A detour sends calls through a wrapper, which runs the patch code and can call the original method. Two separate detours can't control the same entry point, and the second library may replace the first detour. One mod's patch stops running while the game shows no error.

Most RimWorld mods use Harmony, so a Concord mod may target a method that an unrelated Harmony mod has patched.

## How the bridge works

Before Concord patches a method, it checks Harmony. If Harmony hasn't patched the method, Concord installs its normal detour. The normal detour adds no bridge cost.

If Harmony has patched the method, Concord marks it as **contested**. Concord gives Harmony one transpiler at the lowest priority Harmony supports. Each time Harmony rebuilds the method, Concord applies its injections to the instruction stream that Harmony produced.

Harmony keeps control of the entry point while its prefixes and postfixes run. Concord adds its injections to the method body.

| Situation | What Concord does |
| --- | --- |
| Harmony hasn't patched the method | Installs the normal Concord detour |
| Harmony patched the method before Concord applies | Adds one low-priority transpiler and recomposes after each Harmony rebuild |
| Harmony patches the method after Concord applies | Moves the method onto the bridge before Harmony finishes, so both keep running |

## Patches that arrive late

Concord hooks the one method every Harmony patch and unpatch runs through. Harmony calls Concord just before it rebuilds a method, and Concord adds its transpiler to that rebuild. Both mods end up in the finished method.

This works no matter when the Harmony patch shows up. A mod can patch a method during startup, or hours into a game from a settings toggle, and Concord still hears about it first.

Concord applies its patches as soon as a mod asks for them. Earlier versions held them in a queue until mod loading finished, because that was the only way to see Harmony's startup patches in time. The hook replaced that, so the queue is gone.

If Concord can't combine the two patches, it says so. It stops before it touches anything. Harmony takes the method, so Concord's injections don't run on it. Concord reports each method this happens to.

## Setup

Concord enables the bridge when it finds Harmony, so you don't need to set it up.

The bridge lives in a small DLL beside Concord. RimWorld doesn't scan its folder for mod assemblies. Concord loads the DLL after it finds a supported Harmony version in the game. If Harmony isn't present, Concord leaves the bridge unloaded.

You don't need to arrange the two mods in a special order. Concord finds Harmony's startup patches when it applies, and hears about later ones through the hook. If Harmony loads after Concord, Concord waits for it and installs the hook then, before any Harmony patch can run.

If the hook can't install, Concord falls back to how it worked before. It checks for conflicts twice during startup and reports what it finds, but it can't move a method onto the bridge after the fact. The log says which of the two you're in.

## Cases Concord rejects

Concord rejects a few patch patterns because combining them with Harmony could break the method:

- **Methods with Harmony 2.4 inner patches.** Inner prefixes and postfixes target code inside another patch's replacement. Concord can't combine those patches with its own injections.
- **Shared reference-type generic methods.** `Box<string>.Get` may share one compiled method body with every reference-type `Box<T>`. A wrapper for one type could affect the others. Concord rejects that patch. Value-type cases such as `Box<int>.Get` have their own compiled bodies and work.

Concord uses its normal detour and logs a warning in these cases because bridge code may break the game.

Another mod can add a Harmony inner patch after Concord sends the method through the bridge. Concord checks for inner patches during composition, so the watchdog may be the first code to find the problem if no other composition step runs.

## Settings

You can find two switches in Concord's in-game mod settings under `ConcordSettings`:

| Setting | Default | What it does |
| --- | --- | --- |
| Bridge Routing Enabled | On | Turns bridge routing on or off. When off, Concord keeps methods on its normal detour path. Raw-detour conflict reporting works without the bridge. |
| Route Everything When Harmony Present | Off | Sends every method that Concord patches through the bridge when Concord finds Harmony, including methods with no Harmony patches. |

Both settings take effect after you restart the game.

Leave `Route Everything When Harmony Present` off unless you're chasing a specific conflict. Routing a method Harmony never touched is slower than Concord's own detour, and it buys you nothing.

## Check the log

Search the RimWorld log for these markers:

| Marker | Meaning |
| --- | --- |
| `[Concord.Coex] bridge-active` | The bridge loaded and found a supported Harmony version. |
| `[Concord.Coex] hook-installed` | Concord will hear about Harmony patches before they land. This is the healthy state. |
| `[Concord.Coex] hook-unavailable` | Concord couldn't install the hook and can't recover a method after Harmony takes it. The message says why. |
| `[Concord.Coex] routed-contested` | Concord sent a contested method through Harmony. |
| `[Concord.Coex] promoted` | A Harmony patch arrived late and Concord moved that method onto the bridge. Both mods still run. |
| `[Concord.Coex] promote-rejected` | Concord refused to combine the patches and changed nothing before it stopped. |
| `[Concord.Coex] promote-failed` | Concord tried to hand the method over and couldn't finish. |
| `[Concord.Coex] late-contention` | Concord's injections aren't running on a method, with the reason it lost it. |
| `[Concord.Coex] stream-rejected` | Concord couldn't convert Harmony's instruction stream without risk, so it left the stream unchanged. |

If you don't see `bridge-active`, Harmony may be absent or use an unsupported version. Concord's real-binary tests cover Harmony 2.4.x, with no coverage for other lines.

Seeing `hook-installed` and no `promote-` or `late-contention` lines means everything shared a method cleanly.

## Related pages

[How patches work](how-patches-work.md) explains the wrapper Concord builds for a method with no Harmony patches.

[Migrating from Harmony and Prepatcher](migration.md) shows how to move an existing patch to Concord's API.

[Troubleshooting](troubleshooting.md) lists the `CONCxxx` codes that Concord reports when a patch fails.

[Packages](packages.md#concordharmony) covers the `Concord.Harmony` package, which an adapter for another game references to get the same bridge.
