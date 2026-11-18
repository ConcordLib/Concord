# Concord.Ref

**Compile-time reference assembly for [Concord](https://concordlib.dev).**

`Concord.Ref` is a metadata-only package. It exposes Concord's public API so a mod
can be *authored and compiled* against it. It ships **no runtime implementation**.
The target runtime supplies the Concord Assembly (`Concord.dll`) at runtime.

## When to use this

Reference `Concord.Ref` when you are **authoring a mod** for a target runtime that
already ships Concord as its patching runtime. You get the API surface (attributes,
`ControlHandle<T>`, `Patcher`, `AttachedField<,>`, and friends) for IntelliSense and
compilation, without taking a hard runtime dependency on the patching engine.

Don't reference `Concord.Ref` if you are the **runtime adapter** integrating Concord into a target runtime;
use the Concord Assembly directly.

## What the package contains

| Part | What it does |
| --- | --- |
| `ref/net10.0/Concord.dll`, `ref/net472/Concord.dll` | The reference assemblies the mod compiles against |
| A dependency on `Concord.Generators` of the same version | Writes the patch manifest and the `[Shadow]` members into the mod |
| `build/Concord.Ref.props` | Keeps the Concord attributes in the mod's assembly for a .NET Framework target |

Every method body in the reference assembly is a metadata stub (`throw null`). The
assembly has `ReferenceAssemblyAttribute`, so the runtime refuses to load it.
NuGet resolves the `ref/` folder for compilation and contributes nothing at runtime. The
mod never ships a duplicate `Concord.dll`, and the loaded Concord Assembly satisfies binding.

The reference assembly has the same assembly identity (`Concord`) as the Concord Assembly.
A mod compiled against it binds to the target runtime's `Concord.dll` with no redirection.

## The patch manifest

The generator writes a class named `Concord.Generated.PatchManifest` into the mod. The
compiler leaves the Concord attributes out of the mod's assembly, and the manifest describes
them instead. A host that checks attribute types before mod code runs can then load the
assembly. `Patcher.Apply` reads the attributes from the manifest. The mod's source needs
nothing for the manifest, and the mod needs no extra package reference, project property, or build step.

A .NET Framework target keeps the attributes in its assembly and gets the manifest too.

## Add to a project

```xml
<ItemGroup>
  <PackageReference Include="Concord.Ref" Version="1.0.0" />
  <PackageReference Include="Concord.Analyzers" Version="1.0.0" PrivateAssets="all" />
</ItemGroup>
```

`Concord.Analyzers` is optional but recommended. It is build-time tooling only; keep
`PrivateAssets="all"` so it does not flow into mod output or downstream packages.

## Notes

- Targets `net10.0` and `net472`.
- The reference assembly's public surface is clean of MonoMod types; mods compile against
  Concord's API only.
- Pair with `Concord.Analyzers` for IDE/build feedback while authoring patches.

See [Packages](../../docs/packages.md) for how this fits with the other projects.
