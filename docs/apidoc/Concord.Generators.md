---
uid: Concord.Generators
---

Build-time source generators and IDE refactorings for patch declarations. The package writes the patch manifest, `Concord.Generated.PatchManifest`, and the typed members requested with `[Shadow]`. For a .NET Framework target it also writes the patch registry, `Concord.Generated.PatchRegistry`, which an older Concord runtime reads. Its refactorings create patches, injections, and shadow members. `Concord.Ref` depends on this package, so a mod gets it with that reference.
