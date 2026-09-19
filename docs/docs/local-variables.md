# Target a local variable

A target method's local variables hold values that never reach a parameter or a return value. Concord reaches them in two ways:

- A `[Local]` parameter on an injection binds one local, so the injection reads it. A `LocalHandle<T>` parameter also writes it.
- An `At.Local` injection attaches to each read or write of a local, the way an invoke injection attaches to a call.

Both need Concord to pick one slot out of the target body. That choice is what the selectors below do.

## Select the local

Set at most one selector. With none set, the parameter's own type picks the local.

| Selector | Example | What it matches |
| --- | --- | --- |
| Type only | `[Local] int doubled` | The one local of that type |
| `Ordinal` | `[Local(Ordinal = 2)] int doubled` | The second `int`, in slot order |
| `Index` | `[Local(Index = 4)] int doubled` | Raw slot 4 |
| `Name` | `[Local(Name = "doubled")] int doubled` | The local the symbols name `doubled` |

Type only is the default and the one to reach for first. A target with two locals of that type raises `CONC147` rather than a guess.

`Ordinal` counts the locals of the parameter's type in slot order, and skips every other slot. It stays correct while the target declares the same locals of that type in the same order.

`Index` names a raw slot. It is exact, and any recompile of the target can move it. A slot of another type raises `CONC150`.

`Name` reads the target's symbols. It is the least portable selector and it does nothing on RimWorld. See [Selecting by name](#selecting-by-name).

### Selecting by name

`Name` is the least portable selector, and it is not the recommended one. It reads the target's symbols, so it needs a pdb next to the assembly file on disk. Three things break it:

- A host that loads an assembly from bytes leaves `Assembly.Location` empty, so Concord finds no pdb and raises `CONC151`. An embedded pdb does not help, because the byte array is gone by then. RimWorld loads every mod assembly this way.
- `Assembly-CSharp`, the RimWorld game assembly, ships no pdb at all. No adapter can make `Name` reach it.
- One name can cover more than one slot. A debug build gives two `for (int i ...)` loops one name across two slots, which raises `CONC163`.

An adapter fixes the first point with `Patcher.UseLocalNameResolver`, which maps a loaded module to the file its symbols sit beside:

```csharp
Patcher.UseLocalNameResolver(module => ModRoots.PathOf(module));
```

On RimWorld, `Name` resolves nothing until an adapter calls that, and it never reaches the game assembly. Use the type, `Ordinal`, or `Index` there.

## Read a local

A `[Local]` parameter binds a slot and reads it:

```csharp
[Patch]
abstract class ShopPatch : Shop
{
    [Inject(At.Return, nameof(Total))]
    static void Report([Local] int doubled)
    {
        Ledger.Record(doubled);
    }
}
```

A read is legal at `At.Return`, `At.Tail`, `At.Finally`, `At.Local`, and the `At.Head` and `At.Tail` shifts of an invoke or construction injection. Every other position raises `CONC146` or `CONC158`, because the target body has not filled its slots yet, or the injection never sees them.

The parameter gives you a copy. You cannot write the target's local through it. Assigning it raises
`CONCORD049` at compile time and `CONC164` at patch time. So does passing it as a `ref` or `out`
argument. Writing a field of a struct local, such as `spot.X = 999`, raises `CONCORD049` too. That
write lands on the copy, and the target never sees it. Use a `LocalHandle<T>` to write. One parameter also cannot carry both
`[Capture]` and `[Local]`: that raises `CONCORD050` and `CONC165`.

## Write a local

A `LocalHandle<T>` parameter reads and writes the slot through its `Value` property:

```csharp
[Patch]
abstract class ShopPatch : Shop
{
    [Inject(nameof(Total), typeof(Ledger), nameof(Ledger.Record), At.Tail)]
    static void Bump([Local] LocalHandle<int> doubled)
    {
        doubled.Value += 1;
    }
}
```

No handle instance exists at run time. Concord lowers each `Value` read to an `ldloc` and each `Value` write to an `stloc` while it composes the wrapper, and the parameter disappears. Four rules follow from that. The first three raise `CONC161` and the fourth raises `CONC156`:

- Every use is a direct `Value` read or write on the parameter. Storing the handle, passing it to a call, capturing it in a lambda, or returning it are all rejected.
- A `Value` access cannot sit across a conditional. Write `if` and `else` arms with a plain assignment in each one, not `h.Value = flag ? 1 : 2`.
- Nothing constructs a `LocalHandle<T>`. A handle arrives as a parameter or not at all.
- The position must leave the write readable. `At.Return`, `At.Tail`, and `At.Finally` run after the body is done with its locals, so a write there is dead and raises `CONC156`. Use `At.Local` or an invoke or construction shift.

## The At.Local contract

`At.Local` attaches the injection to reads or writes of one local:

```csharp
[Patch]
abstract class ShopPatch : Shop
{
    [Inject(nameof(Total), typeof(int), LocalAccess.Store, At.Local)]
    static int Bump(int total)
    {
        return total + 1;
    }
}
```

The injection is a value injection. It takes the local's type and returns the replacement.

| Part | Meaning |
| --- | --- |
| `LocalAccess.Store` | Matches `stloc` and nothing else. The return value overwrites what the body wrote. |
| `LocalAccess.Load` | Matches `ldloc` and nothing else. The return value replaces the loaded value, and the slot keeps its own. |
| `By` | The 1-based occurrence to target. `0` targets every match. |
| `[Slice]` | Bounds both matching and `By` counting to a range of the body. |

`By` counts against a copy of the target method body, taken before any injection splices into it. One mod's injection never renumbers another mod's occurrences. See [Counting occurrences with by](how-patches-work.md#counting-occurrences-with-by).

### What Store can see

`LocalAccess.Store` matches the `stloc` family and nothing else. That is every assignment written by value, and it is not every write.

A write through the local's address leaves no `stloc`. Concord rejects the part of that it can prove. The body can take the address and hand it straight to `stind`, `stobj`, `cpobj`, or `initobj`. A store injection never fires for that write, so the selection raises `CONC154` rather than composing into a patch that does nothing.

An address handed to a call is a different case, and Concord allows it:

```csharp
int n = 0;
int.TryParse(s, out n);   // writes n through the address, no stloc
```

`At.Local(Store)` on `n` fires once, for the `int n = 0` initializer, and misses the write the author cares about. A mutating method on a struct local is the same shape:

```csharp
Vector2 v = default;
v.Mutate();               // writes v through the address, no stloc
```

Concord does not reject either one. To decide, Concord must prove the called method writes nothing, and `IsReadOnlyAttribute` cannot show that. `Single` and `DateTime` mark the type rather than the member, and the net472 runtime library predates the attribute. Its absence proves nothing, so Concord does not guess.

The rule has to stay this narrow. Almost every instance method call on a struct local takes the local's address. A rule against every address-take would reject `points.ToString()` too, which makes the position unusable on `float`, `int`, `DateTime`, and every user struct.

So read `Store` as the assignments Concord can see. When an injection looks like it never fires, check whether something writes that local through its address.

## Limits

| Limit | What happens |
| --- | --- |
| A release build reuses slots | One slot can carry two same-typed locals in separate scopes, so a bound slot may hold another variable elsewhere in the body. |
| A loop local holds the last iteration | A read at a late position such as `At.Return` gets that value, or the default when the loop never ran. |
| `LocalAccess.Store` misses indirect writes | An `out` or `ref` argument, and a mutating call on a struct local, write through an address and leave no `stloc`, so the injection never fires for them. Concord allows the selection. See [What Store can see](#what-store-can-see). |
| Another mod's transpiler can move a slot or a count | Concord drops a selector that a foreign edit broke. An added slot can make one ambiguous. A `By` that held against the target's own IL can stop holding. The owning mod gets a report, and every other mod on the target stays applied. |
| No Mono.Cecil in the API | Selectors are attributes and plain types. Concord never hands a mod an IL object. |

A dropped selector is the only failure Concord does not throw for. A selector that would have failed
against the target body on its own is your own mistake, so it throws. To see a drop, give Concord a
log at startup with `Patcher.UseLog(message => ...)`. Without one the report goes to
`System.Diagnostics.Trace`, which most hosts do not listen to.

## Diagnostics

| Code | Cause |
| --- | --- |
| `CONC145` | A selector sets more than one of `Ordinal`, `Index` and `Name`. Keep one |
| `CONC146` | A `[Local]` parameter at `At.Head`, which runs before the body assigns a local |
| `CONC147` | The type alone matches more than one local. Set `Ordinal` |
| `CONC148` | No local of that type is in range |
| `CONC149` | `Index` names a slot past the end of the body |
| `CONC150` | `Index` names a slot of another type |
| `CONC151` | `Name` needs symbols the host cannot read |
| `CONC152` | The symbols name no local by that name |
| `CONC153` | The selected slot is pinned or a byref, which cannot bind |
| `CONC154` | A store injection on a slot the body writes through its address with `stind`, `stobj`, `cpobj`, or `initobj` |
| `CONC155` | `By` names an occurrence the body does not have |
| `CONC156` | A `LocalHandle<T>` write at a position where the body is done with its locals |
| `CONC158` | A `[Local]` or `LocalHandle<T>` parameter at a position that binds no local |
| `CONC159` | A load injection on the slot that carries the method's result |
| `CONC160` | A `[Local]` or `LocalHandle<T>` parameter on a whole-method `At.Around` |
| `CONC161` | A `LocalHandle<T>` used as anything other than a direct `Value` read or write |
| `CONC162` | The selected slot has no read and no write in the composed body |
| `CONC163` | `Name` covers more than one slot. Set `Ordinal` or `Index` |
| `CONC164` | An injection assigns to a parameter Concord binds for it, such as `[Local]` |
| `CONC165` | One parameter carries both `[Capture]` and `[Local]` |
