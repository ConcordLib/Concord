# In-game test harness

Repeatable, real-game proof that Concord and Harmony coexist correctly on a shared patch
target. Not part of the mod package (`test/` is excluded via `.steamignore`).

`run-injection-surface-test.sh` and `run-coexistence-test.sh` run in a gamecrate container and
read or write no native RimWorld install. The other two runners still drive the native install,
as noted below.

## What it proves

Two half-mods share one target method, `Concord.RimWorld.CoexTest.SharedTarget.Compute`:

- **ConcordHalfMod** declares the target and a Concord `[Patch]`/`[Inject(At.Head, ...)]` on it,
  applied the way a normal consumer mod applies patches (`Patcher.Apply` from the mod ctor). It
  also schedules a double-deferred `LongEventHandler.ExecuteWhenFinished` callback that invokes
  `Compute(41)` after Concord's own deferred flush has run, logging the result.
- **HarmonyHalfMod** declares a `[StaticConstructorOnStartup]` Harmony postfix on the same
  target, resolved through the AppDomain by name (`Type.GetType("...SharedTarget, CoexConcordHalf")`)
  so it has no compile-time dependency on ConcordHalfMod.

`run-coexistence-test.sh` launches the game once per mode, each in its own gamecrate instance,
and greps the resulting player log for markers proving specific behavior. A third argument,
`--prepatcher`, layers zetrith.prepatcher (workshop `2934420800`) into the mod list, right
after Harmony and before the probes; it composes with every mode but `--harmony-absent`
(prepatcher needs Harmony loaded), and only on 1.4, 1.5 and 1.6, the versions its
`LoadFolders.xml` declares:

- **`--contested`** — both half-mods active alongside Harmony. Proves the bridge routes a
  target patched by both a Concord injection and a foreign Harmony patch end-to-end: the
  Concord head injection runs, the Harmony postfix runs, and the bridge logs
  `[Concord.Coex] host-owned` for the shared target.
- **`--uncontested`** — only ConcordHalfMod active, with Harmony present but never touching the
  shared target. Proves the bridge hooks every loaded Harmony copy
  (`[Concord.Coex] copier-hook-installed`), but a target nothing else patches stays on the raw
  (non-bridge) path — no `host-owned` for it.
- **`--harmony-absent`** — only ConcordHalfMod active, Harmony not loaded at all. Proves the
  bridge never activates without Harmony present (no `copier-hook-installed`, no `host-owned`,
  zero Harmony execution) and Concord's own patching still works unassisted.
- **`--late-harmony`** (`HarmonyLateMod`) — ConcordHalfMod active alongside a probe that holds
  its Harmony patch until five seconds after the main menu is up. The probe applies the patch on
  its timer thread. It calls `LongEventHandler.ExecuteWhenFinished`, and with no long event in
  progress that method runs the action right away on the calling thread. Every other mode
  patches during load, so this is the only one where Harmony arrives on a target Concord has
  already been detouring and running on. Proves the rebuild notifier catches it: the probe's own
  invoke logs `[COEX] late-invoke result=42 head-count=2`, which is Concord's head firing a
  second time inside the wrapper Harmony built after the fact, and the Harmony postfix runs too.

- **`--prepatcher`** composes with any of the modes above (except `--harmony-absent`) on 1.4,
  1.5 and 1.6. Prepatcher reloads `0Harmony` and restarts the game in a second pass before the
  probes load, so Concord must install again after that restart and patch correctly in the pass
  that follows. The log must show no `ReflectionOnly api` error and no
  `Error while instantiating a mod of type Prepatcher`, and `[Concord.RimWorld] Concord v` must
  appear twice, once per pass, with a `[Concord.MonoMod] backend:` line after the second one.

Every mode additionally asserts the composed result is correct (`Compute(41)` returns `42` with
exactly one Concord head firing at startup), and that Concord never logged losing a method:
no `[Concord.Coex] hook-unavailable`, `copier-hook-unavailable` or `arrival-failed`.

Every mode that loads Harmony also requires the notifier hook to arm on at least one copy
(`[Concord.Coex] hook-installed`). The bridge logs one of three markers per Harmony copy it finds:

| Marker | Logged for | Fails the run |
|---|---|---|
| `hook-unavailable` | A live copy is refused whole. Nothing made through that copy is seen. | Yes |
| `hook-degraded` | A copy arms with one feature off, usually an unreadable shared patch state. | No |
| `hook-skipped` | A reflection-only copy. The bridge leaves it dead and alone. | No |
| `[Concord] declaration error` | A Concord patch failed to compose. The log's CONC line above it names the reason. | Yes |

Two further modes prove what happens when a *third* mod applies its own Harmony patch to the
same target *after* Concord has already routed it through the bridge (i.e. after the contested
baseline in `--contested` has already been proven):

- **`--second-harmony`** (`HarmonySecondMod`) — at a defer level after ConcordHalf's own
  double-deferred probe, applies a second, ordinary Harmony postfix to the shared target, then
  invokes `Compute(41)` again one defer level later. Any `Harmony.Patch` call forces Harmony to
  rebuild the method, which re-runs Concord's lowest-priority transpiler against the fresh
  instruction stream. Because a postfix is a composable construct, Concord's transpiler
  recomposes cleanly: `concord-head` fires *again* on the post-rebuild method (head-count goes
  from 1 to 2), and both the first and second Harmony postfixes still run.
- **`--second-harmony-inner`** (`HarmonyBreakerMod`) uses the same defer shape. Instead of a
  postfix, it installs a Harmony *transpiler* that marks the method body with a `BeginFaultBlock`
  exception region. On Waylith, Concord owns the body and evicts the breaker's illegal
  transpiler. It logs `injection-evicted` with a link to file a bug report, then runs its own
  head injection undisturbed (`head-count=2`). On the mono backends, Concord's converter composes
  a `BeginFaultBlock` region instead of rejecting it, so both transpilers run against the same
  instruction stream. The breaker's fault block still applies, and Concord's head fires again on
  the rebuilt method (`breaker-invoke result=42 head-count=2`). Both backends keep the *other*
  mod's Harmony postfix (`HarmonyHalfMod`) working, and the target still returns the correct
  value.

## Running it

```bash
./test/ingame/run-injection-surface-test.sh 1.6     # any of 1.1 .. 1.7, 1.6 is the default
```

The run builds Concord for that version, builds the probe mod against the same version's
reference assemblies, then launches the `concord` profile headless in a gamecrate container and
waits for the probe's `[SURF] done` marker (up to 420 seconds). It prints
`PASS(<version>): ...` on success, or exits `1` with the failed assertion and a log tail.

The game log stays where gamecrate put it:
`~/.local/share/gamecrate/rimworld/concord/instances/surf-<version>/logs/runs/<timestamp>/Player.log`.
Read it with `rwlog`, never `cat`.

Local game images cover 1.1, 1.2, 1.3 and 1.7 today. A missing one fails before launch, naming
the exact command to build it: `gamecrate steam build --game rimworld --variant linux --beta <branch>`.

The other two runners (`run-attached-field-test.sh`, `run-cosmere-regression.sh`) still drive
the native install and still pin 1.6.

## The shared harness

`lib/gamecrate-harness.sh` holds the setup, so a runner is its scenario and nothing else.
Source it and call:

| Function | Does |
|---|---|
| `concord_build <version>` | builds the mod into `<version>/Assemblies` |
| `probe_build <csproj> <version>` | builds one probe mod against that version's refs |
| `scenario_run <version> <scenario> <marker> <timeout> <mod>...` | launches, returns gamecrate's exit status |
| `scenario_log` | the `Player.log` the last run wrote |

A `<mod>` is a package id the gamecrate config knows, like `brrainz.harmony`, or
`path:<absolute dir>` for a probe mod that lives in this tree. Concord itself always loads from
this checkout, so no caller names it. Each scenario gets its own gamecrate instance, named
`<scenario>-<version>`, with its own saves, logs and container. Set `CONCORD_GAME_IMAGE` to
launch a specific image instead of the version's default tag.
