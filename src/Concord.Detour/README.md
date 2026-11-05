# Concord.Detour

The seam between Concord and whatever backend puts a composed body on a live method.
Where `Concord.Emit` builds the composed body, this layer orders the injections that
go into it and holds the one backend installed for the process.

It ships no engine of its own. The runtime adapter supplies one and sets
`DetourBackend.Current` at install. Read `Current` before that, and it throws.

## What's here

| Type | Does |
| --- | --- |
| `IDetourBackend` | The backend contract: `ApplyComposed`, `PatchedTargetCount`, `OwnersOf`. |
| `DetourBackend.Current` | The one backend for the process. |
| `IDetourHandle` | Reports a target's applied state; `Dispose()` drops the injections that handle owns. |
| `InjectionOrderer` | Topologically sorts injections into composition order. |
| `MethodIdentity` | Canonical routing keys for a target, shared across its generic instantiations. |
| `AssemblyImage` | Rewrites a module version id so the same file can load twice in memory. |

`IOwnsBodyQuery` and `IRecomposeTarget` are optional extras a backend may implement.

## The backend contract

`IDetourBackend.ApplyComposed` is the only way in. There is no plain, single-injection
`Apply`. Every call composes the target's whole live injection set and republishes it.

`DetourBackend.Current` holds the one backend for the process. The runtime adapter
sets it inside `AdapterEntryPoint.Install`, before the first patch applies. Read
`Current` before that runs, and it throws `InvalidOperationException`.

An `IDetourHandle` tracks one caller's own injections on a target. `IsApplied` reports
whether they are still live. `Dispose()` removes only the injections that handle
owns, and republishes the target with whatever is left.

## When to use this

You don't reference it directly. It compiles separately but merges into the Concord assembly (`Concord.dll`). Work here if you are developing how Concord orders injections, or the contract a runtime adapter's backend has to meet.

See [Packages](../../docs/packages.md) for how this fits with the other projects.
