# Concord Assembly

The shipped artifact: one `Concord.dll`.

This project doesn't hold source. It ILRepack-merges the five core libraries (`Concord.Emit`, `Concord.Backend`, `Concord.AttachedData`, `Concord.Orchestration`, `Concord.Enums`) into a single `Concord.dll`, with Mono.Cecil folded in. Build output lands in `Assemblies/`.

## When to use this

This is the Concord Assembly. A target runtime loads it to provide patching, and a mod binds to it at runtime to run its patches. Everything `Concord.Ref` declares at compile time is implemented here.

- Integrating Concord into a target runtime: ship and load this `Concord.dll`. The `Concord.Runtime` package depends on `Concord.Generators` of the same version, so the host's own `[Patch]` classes get a patch manifest. Do not add a second reference to `Concord.Generators`.
- Writing a mod: don't ship this yourself. Compile against [`Concord.Ref`](../Concord.Ref/README.md); the target runtime's Concord Assembly runs your patches.

The merge to one file matters because a host loads each mod assembly into an isolated context. A Concord made of several assemblies would fail to resolve its own dependencies there.

See [Packages](../../docs/packages.md) for the full project map.
