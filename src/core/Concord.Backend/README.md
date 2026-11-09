# Concord.Backend

The seam between Concord and whatever backend puts a composed body on a live method.
Where `Concord.Emit` builds the composed body, this layer orders the injections that
go into it and holds the one backend installed for the process.

It ships no engine of its own. The runtime adapter supplies one and sets
`PatchBackend.Current` at install. Read `Current` before that, and it throws.

## What's here

| Type | Does |
| --- | --- |
| `IPatchBackend` | The backend contract: `Name`, `PatchedTargetCount`, `Judge`, `ApplyComposed`, `OwnersOf`, `OwnsBody`, `Recompose`. |
| `PatchBackend.Current` | The one backend for the process. |
| `IBackendHandle` | Reports a target's applied state; `Dispose()` drops the injections that handle owns. |
| `TargetVerdict` | What `Judge` decided, with the code and message a refusal carries. |
| `InjectionOrderer` | Topologically sorts injections into composition order. |
| `MethodIdentity` | Canonical routing keys for a target, shared across its generic instantiations. |
| `AssemblyImage` | Rewrites a module version id so the same file can load twice in memory. |

## The backend contract

`IPatchBackend` is flat: every backend answers all seven members. `ApplyComposed` is
the only way in. There is no plain, single-injection `Apply`. Every call composes the
target's whole live injection set and republishes it.

`Judge` answers whether a target can carry a patch at all, before any body is built.
`TargetVerdict.Accept()` says it can, and `TargetVerdict.Refuse(code, message)` carries
the diagnostic the caller logs and throws.

`PatchBackend.Current` holds the one backend for the process. The runtime adapter
sets it inside `AdapterEntryPoint.Install`, before the first patch applies. Read
`Current` before that runs, and it throws `InvalidOperationException`.

An `IBackendHandle` tracks one caller's own injections on a target. `IsApplied` reports
whether they are still live. `Dispose()` removes only the injections that handle
owns, and republishes the target with whatever is left.

## When to use this

You don't reference it directly. It compiles separately but merges into the Concord assembly (`Concord.dll`). Work here if you are developing how Concord orders injections, or the contract a runtime adapter's backend has to meet.

See [Packages](../../docs/packages.md) for how this fits with the other projects.
