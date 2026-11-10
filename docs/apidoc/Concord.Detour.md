---
uid: Concord.Detour
---

The detour backend. `IDetourBackend` redirects a target method to a replacement or wrapper. The default `MonoModDetourBackend` uses MonoMod.Core. A handle from `ApplyComposed` owns its added injections. Disposing it removes those injections. Concord rebuilds the wrapper when others remain and removes the detour when none do.
