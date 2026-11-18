# Concord.Generators

Roslyn source generators and refactorings for [Concord](https://concordlib.dev) patch declarations.

- **Patch manifest**: writes the class `Concord.Generated.PatchManifest` into the mod. It describes
  each Concord attribute in the source, and `Patcher.Apply(assembly)` reads the attributes from it.
  An attribute use it cannot write is error `CONC187`.
- **Patch registry**: for a .NET Framework target, writes the class `Concord.Generated.PatchRegistry`
  and an assembly-level `[PatchRegistry]` that names it. The registry lists each `[Patch]` class, so
  an older Concord runtime finds them. A mod with its own `[assembly: PatchRegistry]` gets none.
- **Shadow members**: `[Shadow("member")]` on a partial patch declaration generates the matching
  `[InjectField]` / `[InjectProperty]` / `[InjectMethod]` declaration from target metadata.
- **Scaffolding refactorings**: `Concord: add injection`, `Concord: add shadow member`,
  `Concord: convert to patch declaration`, `Concord: create patch for X()` in any Roslyn IDE.

`Concord.Ref` depends on this package of the same version, so a mod gets it from its
`Concord.Ref` reference. Do not add a second reference to it.

If your project already references this package, remove that reference. A reference to an older
version than `Concord.Ref` stops the restore with the package downgrade error `NU1605`.
