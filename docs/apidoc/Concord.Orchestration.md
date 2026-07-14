---
uid: Concord.Orchestration
---

`Concord.Orchestration` contains the declaration scanner and its support types. `PatchDeclarationScanner` sends `[Inject]` methods to the patch applier. It registers plain non-static fields through the attached-property registry. The author-facing `Patcher`, `PatchBuilder`, and `IPatchHandle` types live in the root `Concord` namespace.
