# Attached Data

Use attached data when a patch needs to store state for a target object. That state is not a real field on the target type. Concord stores the value beside the object instead of changing its type.

`AttachedField<TTarget, TValue>` provides that storage. Each `AttachedField` instance owns a separate table keyed by target object.

## Store attached data

### Create an `AttachedField`

`TTarget` must be a reference type. Keep the `AttachedField` in a static field so the table remains available for as long as the patch needs it:

```csharp
using Concord.AttachedData;

private static readonly AttachedField<GameActor, int> BonusArmor = new();
```

Two `AttachedField` instances do not share values, even when they use the same target and value types. Keys use object identity, so two targets that compare equal still have separate entries.

### Set and read a value

`Set` stores a value for one target. Calling it again replaces the old value.

```csharp
BonusArmor.Set(actor, 5);
int armor = BonusArmor.Get(actor);
```

`Get` returns `default(TValue)` when the target has no entry. For an `int`, that value is `0`. For a reference type, it is `null`.

Use `TryGet` when a missing entry must mean something different from a stored default value:

```csharp
if (!BonusArmor.TryGet(actor, out int armor))
{
    armor = ReadStartingArmor(actor);
    BonusArmor.Set(actor, armor);
}
```

Use `GetOrAddRef` when the value is updated in place. It creates the entry with `default(TValue)` when the target has no entry, then returns a reference to it:

```csharp
BonusArmor.GetOrAddRef(actor) += 5;
```

Calling `Set(actor, 0)` keeps an entry in the table. `TryGet` then returns `true` with the value `0`. `AttachedField` has no method that removes one entry by hand.

### Use it inside a patch

The current target instance is the table key:

```csharp
[Patch]
abstract class ActorExtensions : GameActor
{
    private static readonly AttachedField<GameActor, int> DamageTaken = new();

    [Inject(At.Tail, nameof(TakeDamage))]
    private void AfterTakeDamage(int amount)
    {
        int total = DamageTaken.Get(this) + amount;
        DamageTaken.Set(this, total);
    }
}
```

Each `GameActor` gets its own value. The [Common Tasks](common-tasks.md#use-attached-data-from-a-patch) page has another patch example.

## Storage behavior

### Lifetime

`AttachedField` uses a `ConditionalWeakTable`. The table does not keep a target object alive. Once no other code can reach the target, its table entry can be collected.

The `AttachedField` instance owns the table. If that instance becomes unreachable, its table can be collected even while the target objects remain alive. A `static readonly` field keeps the table reachable while the patch assembly is loaded.

Disposing a patch handle removes its injections. It does not clear a static `AttachedField`, so the stored values remain available until their targets or the table can be collected.

### Persistence

Core keeps attached values in memory. It does not write them to a save file or restore them after a reload. `AttachedField` has no persistence API.

An `[Attached]` field is different: Core hands its storage to the runtime adapter, and an adapter that supports save files persists it. See [Attached fields on patch declarations](#attached-fields-on-patch-declarations).

### Cost and concurrency

Each `Get`, `Set`, or `TryGet` performs a table lookup. Store the result in a local when one injection uses the value more than once.

`AttachedField` does not make a read-modify-write sequence atomic. Two threads can both read the same value before either writes its update. Protect that sequence with the locking rules used by the target runtime when several threads may change the same target.

## Choose the right kind of field

| You need to | Use |
| --- | --- |
| Store new state for each target instance, written as a normal field | `[Attached]` on a field of the patch declaration |
| Store new state from code that is not a patch | `AttachedField<TTarget, TValue>` |
| Read or write a real field already declared on the target type | `[InjectField]` |

If the project references `Concord.Generators`, `[Shadow("fieldName")]` can generate the typed `[InjectField]` declaration on a partial patch class, as shown in [Generate private member declarations](common-tasks.md#generate-private-member-declarations).

### Attached fields on patch declarations

Mark a field `[Attached]` to give every target instance its own copy of that field. The target type does not declare it, so Concord stores it in a side table and rewrites each access:

```csharp
[Patch]
abstract class ActorArmor : GameActor
{
    [Attached]
    public int BonusArmor;

    [Inject(At.Tail, nameof(TakeDamage))]
    private void AfterTakeDamage(int amount)
    {
        BonusArmor += amount;
    }
}
```

`BonusArmor += amount` compiles to a normal field access. At patch time Concord lowers the read to `AttachedStorage.Get`, the write to `AttachedStorage.Set`, and a `ref` to `AttachedStorage.GetOrAddRef`. The value starts at `default(TValue)` for each instance, and it goes away when that instance is collected.

Rules for an `[Attached]` field:

- It must be an instance field. A static field on a declaration is just a static field; leave it unmarked.
- The target type must be a reference type. Attached state is keyed by instance identity.
- The target type must not already declare a field of that name. That case is a shadow field, so drop the attribute.

A field that matches nothing on the target type and carries no attribute is an error, `CONC003`. Concord cannot tell whether you meant to shadow a field, attach a new one, or misspelled a name, so it asks.

Persistence is the adapter's call. An adapter that supports save files writes the field into the save and reads it back. One with no save support keeps the value in memory. Each adapter also decides which target types it can reach. Check its documentation before you rely on a field being saved. It warns at startup when it cannot save one.

### Receive declarations in an adapter

`Patcher.Apply` registers every `[Attached]` field it finds. A host installs its own registry to see them:

```csharp
Patcher.UseAttachedPropertyRegistry(new MyRegistry());
```

`MyRegistry` implements `IAttachedPropertyRegistry`. Concord calls `RegisterAttachedProperty(baseType, name, valueType, slot)` once per declared field, for every assembly passed to `Patcher.Apply`. The `slot` is the same storage the patched code reads and writes, so an adapter that saves the value writes back through it on load.

Install the registry before the first `Patcher.Apply` call. Declarations registered before it is installed go to the default store, which nothing reads, and are lost.
