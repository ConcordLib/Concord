# Concord.AttachedData

Weak-reference side storage, one of the core libraries internal to Concord.

`Concord.AttachedData` lets a mod hang custom data on target instances without adding real fields to their types. It backs the attached-data feature.

## What's here

- `AttachedField<TTarget, TValue>` is a weak-reference table keyed by target instance. Reads and writes are O(1) amortized, and an entry is collected automatically when its target is.

The data lives in a side table rather than on the object, so target types are never modified. The trade-off is boxing for value types and undefined enumeration order. See [Attached Data](../../docs/attached-data.md) for the usage pattern and the determinism caveat.

## The patch manifest sink

A mod's `Concord.Generated.PatchManifest` describes the Concord attributes that the compiler left out of the mod's assembly. Its `Describe(object sink)` method passes each attribute to `IPatchManifestSink`, the public interface in this project. The internal `AttributeStore` keeps what the manifest passes. Each reader of a Concord attribute asks the store first, and reads the assembly's metadata only when the store has nothing.

| Method | What it records |
| --- | --- |
| `OnAssembly(Attribute)` | An attribute on the assembly |
| `OnMember(string memberId, Attribute)` | An attribute on a type, field, property, event, or method |
| `OnParameter(string memberId, int position, Attribute)` | An attribute on the parameter at a zero-based position of a method |
| `OnUnreadable(string memberId, Exception error)` | A type whose attributes name a type that failed to load |

A member id is the string that the internal `MemberIds` reads. It is close to a documentation comment ID, such as `T:My.Patch` or `M:My.Patch.Head(System.Int32@)`. Only a conversion operator has a return type suffix, such as `M:My.Money.op_Implicit(My.Money)~System.Int32`.

## When to use this

It compiles separately but merges into the Concord Assembly (`Concord.dll`). Mods declare attached data through `[Patch]` declarations rather than calling this layer. To read one of those fields from code outside the declaration, `AttachedStorage.SlotFor` plus `SlotAt` returns the slot the patch already uses. See [Attached Data](../../docs/attached-data.md). Work here if you are developing the storage layer.

See [Packages](../../docs/packages.md) for how this fits with the other projects.
