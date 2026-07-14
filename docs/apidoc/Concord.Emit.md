---
uid: Concord.Emit
---

The IL composition layer. `WrapperComposer` and `BodyCopier` build a wrapper from the original body and its patches. This namespace also defines `InjectAt`, control-handle lowering, and the `Operation` family. Rejected patches fail here with a `CONCxxx` code. Most mod authors do not use these types directly.
