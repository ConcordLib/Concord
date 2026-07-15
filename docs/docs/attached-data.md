# Attached Data

Use attached data when a patch needs to store state for a target object, but that state is not a real field on the target type. Concord stores the value beside the object instead of changing its type.

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

Core keeps attached values in memory. It does not write them to a save file or restore them after a reload. A runtime adapter may provide save support for its own object model, but `AttachedField` has no persistence API.

### Cost and concurrency

Each `Get`, `Set`, or `TryGet` performs a table lookup. Store the result in a local when one injection uses the value more than once.

`AttachedField` does not make a read-modify-write sequence atomic. Two threads can both read the same value before either writes its update. Protect that sequence with the locking rules used by the target runtime when several threads may change the same target.

## Choose the right kind of field

| You need to | Use |
| --- | --- |
| Store new state for each target instance | `AttachedField<TTarget, TValue>` |
| Read or write a real field already declared on the target type | `[InjectField]` |
| Declare attached-property metadata for a runtime adapter | A plain instance field on the patch declaration |

If the project references `Concord.Generators`, `[Shadow("fieldName")]` can generate the typed `[InjectField]` declaration on a partial patch class, as shown in [Generate private member declarations](common-tasks.md#generate-private-member-declarations).

### Plain fields on patch declarations

The declaration scanner registers plain instance fields as attached-property metadata:

```csharp
[Patch]
abstract class ActorData : GameActor
{
    public int BonusArmor;
}
```

Core does not rewrite reads or writes of `BonusArmor` into `AttachedField` calls. A runtime adapter may use the registered name and type for its own integration.

Use `AttachedField` when the patch needs live side storage. Use `[InjectField]` when `BonusArmor` already exists on `GameActor`. `PatchDeclarationScanner` skips static fields and fields marked `[InjectField]` when it registers attached-property metadata.
