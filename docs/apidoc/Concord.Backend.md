---
uid: Concord.Backend
---

The seam between Concord and the backend that puts a composed body on a live method. `IPatchBackend` is the contract every backend implements. `PatchBackend.Current` holds the one backend for the process, and the runtime adapter sets it at install. A handle from `ApplyComposed` owns its added injections. Disposing it removes those injections. `InjectionOrderer` sorts injections into composition order.
