# Harmony compatibility

Concord's RimWorld adapter can share a method with Harmony patches without extra setup. If Concord can't combine the patches, it logs a warning and uses its normal detour.

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
| Harmony patches the method after Concord applies | Reports late contention; Concord's injections stop running on that method |

The bridge must see the Harmony patch before Concord installs its normal detour, so Concord waits until mod constructors and static constructors finish. This delay lets Concord see the Harmony patches that mods register during startup.

A Harmony patch can arrive after Concord installs its detour, but Concord doesn't switch that method to the bridge during the same game run. A watchdog checks for this case and logs the conflict.

## Setup

Concord enables the bridge when it finds Harmony, so you don't need to set it up.

The bridge lives in a small DLL beside Concord. RimWorld doesn't scan its folder for mod assemblies. Concord loads the DLL after it finds a supported Harmony version in the game. If Harmony isn't present, Concord leaves the bridge unloaded.

You don't need to arrange the two mods in a special order. Concord finds Harmony's startup patches when its delayed patch queue runs. The late-contention rule applies if a mod adds a Harmony patch after Concord has installed its detour.

A small group of Concord's own patches must run during mod loading, before the delayed queue. Concord manages those patches for you.

## Cases Concord rejects

Concord rejects a few patch patterns because combining them with Harmony could break the method:

- **Constructor `Around` injections.** A whole-method `Around` needs a complete object before it can wrap the constructor call. Harmony's constructor patch doesn't promise that state.
- **Methods with Harmony 2.4 inner patches.** Inner prefixes and postfixes target code inside another patch's replacement. Concord can't combine those patches with its own injections.
- **Async and iterator methods with the wrong entry method.** Concord must patch the generated state-machine method instead of the method you see in source, and it stops if it receives the wrong one.
- **Shared reference-type generic methods.** `Box<string>.Get` may share one compiled method body with every reference-type `Box<T>`. A wrapper for one type could affect the others. Concord rejects that patch. Value-type cases such as `Box<int>.Get` have their own compiled bodies and work.
- **Injection code that calls `Assembly.GetExecutingAssembly()`.** Harmony changes that call to return the target's assembly, so code that expects the injection assembly would get the wrong result.

Concord uses its normal detour and logs a warning in these cases because bridge code may break the game.

Another mod can add a Harmony inner patch after Concord sends the method through the bridge. Concord checks for inner patches during composition, so the watchdog may be the first code to find the problem if no other composition step runs.

## Settings

You can find two switches in Concord's in-game mod settings under `ConcordSettings`:

| Setting | Default | What it does |
| --- | --- | --- |
| Bridge Routing Enabled | On | Turns bridge routing on or off. When off, Concord keeps methods on its normal detour path. Raw-detour conflict reporting works without the bridge. |
| Route Everything When Harmony Present | Off | Sends every method that Concord patches through the bridge when Concord finds Harmony, including methods with no Harmony patches. |

Both settings take effect after you restart the game.

## Check the log

Search the RimWorld log for these markers:

| Marker | Meaning |
| --- | --- |
| `[Concord.Coex] bridge-active` | The bridge loaded and found a supported Harmony version. |
| `[Concord.Coex] routed-contested` | Concord sent a contested method through Harmony. |
| `[Concord.Coex] flush-complete` | Concord finished its delayed patch queue after mod loading. |
| `[Concord.Coex] late-contention` | Harmony patched a method after Concord installed its detour. Concord's injections aren't running on that method. |
| `[Concord.Coex] stream-rejected` | Concord couldn't convert Harmony's instruction stream without risk, so it left the stream unchanged. |

If you don't see `bridge-active`, Harmony may be absent or use an unsupported version. Concord's real-binary tests cover Harmony 2.4.x, with no coverage for other lines.

## Related pages

[How patches work](how-patches-work.md) explains the wrapper Concord builds for a method with no Harmony patches.

[Migrating from Harmony and Prepatcher](migration.md) shows how to move an existing patch to Concord's API.

[Troubleshooting](troubleshooting.md) lists the `CONCxxx` codes that Concord reports when a patch fails.
