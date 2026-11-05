# Concord.Host.Adapter

Concord's stage in WaylithCLR's method-body transformer pipeline. The pipeline runs
on the forked CoreLib that RimWorld 1.7 ships on WaylithCLR 10.0.15.

## What it does

`ConcordStage` registers with `WaylithClr.MethodBodyTransformer`. It owns no body of
its own. On a rebuild, the pipeline hands the stage the previous stage's output, or
the target's default body, and the stage composes the target's live injections on
top of that input.

The composed body becomes the target's own body. A stack trace names the target
itself. Reflection on the target returns the target. No wrapper method exists
anywhere in the process.

## When it runs

`AdapterEntryPoint.Install` checks `RuntimeCapability.Support` first. That check
needs `WaylithClr.MethodBodyUpdater.IsSupported` to report true and the
`WaylithClr.MethodBodyTransformer` type to be present. If either is missing,
`Install` refuses with one `[Concord.Waylith]` line and does nothing else. No
backend is installed, so `DetourBackend.Current` throws and nothing Concord is
asked to patch gets patched.

## Priority

`ConcordStage` runs at priority `-1000`, the lowest in the pipeline. Stages run in
ascending priority order, so this guarantees Concord sees the target's default body
on every rebuild. A stage that ran after some other transformer would see that
transformer's output instead, and Concord's injections anchor into offsets of the
target's own IL, not into whatever another stage reshaped it into.

## A failed transform

A compose, encode, or write failure inside the stage throws. The pipeline reports the
failure back through `Rebuild`. The adapter logs one line under the
`[Concord.Waylith]` marker, and every handle on that target reports `IsApplied` as
`false`. Nothing else in the process breaks. The target keeps running its last good
body, or its original body if it never had one, and the next change to its injection
set tries the transform again.

## Compiling

The adapter compiles against WaylithCLR's own framework assemblies, imported through
`build/Fork.targets`. Point `-p:ForkRuntimeDir=<path>` at a runtime layout, or let it
default to `/home/aaron/.cache/waylith-earlybuild`, the early build used today. A
gamecrate-built RimWorld image will supply that directory later, once gamecrate's
`refs` command stops dropping the fork's framework DLLs.

`ForkApi` is the one class in this assembly that names a WaylithClr type directly.
Every member on it carries `[MethodImpl(MethodImplOptions.NoInlining)]`, so a stock
runtime never JITs a call into a type it does not have.

## Running the tests

Fork facts need the fork runtime and run as a console app:

```bash
dotnet run --project tests/Concord.Host.Adapter.ForkTests/Concord.Host.Adapter.ForkTests.csproj -c Release -f net10.0 [-- -class <fqn>]
```

The runner prints the runtime it ran on, `10.0.15-waylithclr.dev` today.

`tests/Concord.Host.Adapter.ForkTests/run-fork-tests.sh` builds and runs the same facts. Set
`FORK_FACTS_COVERAGE=<file>` to run them under `coverlet` and write an OpenCover report, which
is how the SonarCloud job counts the fork-only adapter code as covered.

Unit tests run on stock .NET 10, through the xunit console runner:

```bash
cd tests/Concord.Host.Adapter.Tests/bin/Release/net10.0
DOTNET_ROLL_FORWARD=LatestMajor dotnet ~/.nuget/packages/xunit.runner.console/2.9.3/tools/net6.0/xunit.console.dll Concord.Host.Adapter.Tests.dll -parallel none -nologo
```
