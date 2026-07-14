---
uid: Concord.AttachedData
---

Weak-reference storage for attaching custom data to live target instances without changing their types. `AttachedField<TTarget, TValue>` is a side table keyed by target instance. The table drops an entry when its target is collected. Core does not persist values; a runtime adapter may add persistence.
