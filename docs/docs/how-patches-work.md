# How patches work

A Concord patch changes what runs when code calls a target method. Concord puts a copy of the target body and its injections in a wrapper method, then points the target at that wrapper. The source file and DLL stay unchanged.

## A patch from start to finish

Suppose a game has this method:

```csharp
public int GetPrice()
{
    return 10;
}
```

This patch adds 5 to the result:

```csharp
[Patch]
abstract class PricePatch : ShopItem
{
    [Inject(At.Tail, nameof(GetPrice))]
    private void AfterGetPrice(ControlHandle<int> ch)
    {
        ch.ReturnValue += 5;
    }
}
```

When you apply the patch, Concord finds `ShopItem.GetPrice()` and copies its body into a new wrapper. It places `AfterGetPrice` before the method's last `return`, then installs a detour from `GetPrice` to the wrapper.

For declarative patches, `Patcher.Apply` reads the patch registry built by `Concord.Generators`; without the generator, Concord finds the same `[Patch]` classes through reflection.

The wrapper behaves like this:

```csharp
public int GetPrice__ConcordWrapper()
{
    int result = 10;
    result += 5;
    return result;
}
```

That code is a C# sketch of the result. Concord emits the wrapper at runtime. It does not create a method with that name in the target assembly.

Callers still call `GetPrice()`. The detour sends those calls to the wrapper, so they receive `15`. If you remove this patch while other injections remain, Concord rebuilds the wrapper without it. Once no injections remain, Concord removes the detour.

## Injection positions

Target-method injections fit into the body like this:

```text
Head
original target body
  Return before each return
  Tail before the last return
```

Invoke patches a call inside the body. Argument and Constant handle narrower targets.

| Position | Runs at | Common use |
| --- | --- | --- |
| Head | Before the target body | Check inputs or cancel the call |
| Return | Before each `return` | Inspect or replace each result |
| Tail | Before the last `return` | Change the final result on the main exit |
| Around | Around the whole target body | Choose if and when the body runs |
| Invoke | Before, after, or around a matched call | Change one call inside the target |
| Argument | At one argument of a matched call | Replace that argument |
| Constant | At a matched literal | Replace a compiled constant |

Start with Head and Tail. Use Return when the method has early exits and you need to handle each result. Tail and Return act the same on a method with one `return`.

The position comes first in a target-method attribute:

```csharp
[Inject(At.Tail, nameof(GetPrice))]
```

The builder API can select the target-method positions:

```csharp
Patcher.For(typeof(ShopItem), nameof(ShopItem.GetPrice))
       .Tail(typeof(PricePatch), "AfterGetPrice")
       .Apply();
```

The builder has `.Head(...)`, `.Return(...)`, `.Tail(...)`, `.Around(...)`, and `.Invoke(...)`. Its general `.Inject(...)` overload supports Head, Return, Tail, and Around. Constant and Argument use their attribute forms or the low-level `Injection` API.

## Patch declarations

A patch declaration can inherit from its target type:

```csharp
[Patch]
abstract class PricePatch : ShopItem
{
}
```

The base type lets C# bind target members through its normal access rules. `[Patch]` tells Concord to scan the type for injections. Concord treats the class as a declaration and does not construct a `PricePatch` object.

### Sealed and non-subclassable targets

Use `[Patch(typeof(...))]` when the target is sealed or cannot be a base class. Injected member declarations give the patch access to the target object and its members:

```csharp
[Patch(typeof(SealedGameEntity))]
abstract class SealedEntityPatch
{
    [InjectInstance]
    protected abstract SealedGameEntity Self { get; }

    [InjectField("_fuel")]
    private int fuel;

    [InjectProperty("Mood")]
    protected abstract int Mood { get; set; }

    [InjectMethod("Recalculate")]
    protected abstract int Recalculate(int add);

    [Inject(At.Tail, nameof(SealedGameEntity.Tick))]
    private void AfterTick(ControlHandle ch)
    {
        fuel++;
        Mood = Math.Max(0, Mood - 1);
        Recalculate(5);
        Log.Message(Self.ToString());
    }
}
```

`[InjectInstance]` supplies the current target object. The other attributes map each declaration to a real field, property, or method on the target. The declared types and signatures must match.

`Concord.Generators` can write the `[InjectField]`, `[InjectProperty]`, and `[InjectMethod]` declarations from `[Shadow]` attributes on an abstract partial patch class. The generated members use the target member's type or signature. Patch code can use them through normal C# expressions such as `this.fuel`, `this.Mood`, and `this.Recalculate(5)`. See [Generate private member declarations](common-tasks.md#generate-private-member-declarations).

Concord reports `CONC071` when it cannot find an injected member. It reports `CONC072` when the declaration has the wrong type, static form, return type, or signature.

The declaration scanner records plain instance fields without `[InjectField]` as attached-property metadata. Concord Core does not turn reads and writes of those fields into side-table access. Use `AttachedField<TTarget, TValue>` for live [side storage](attached-data.md).

`Concord.Analyzers` checks patch declarations when it can resolve the target type. It catches missing members and bad signatures before runtime. It also checks overloads, control handles, static targets, and duplicate injections. Assembly-qualified string targets work when a project reference exposes the target assembly.

The analyzer suggests `typeof` or `nameof` when you can use them. It also suppresses the C# field-use warnings that `[InjectField]` declarations would cause.

## Cancelling a call or changing its result

A Head injection can return `Control`:

```csharp
[Inject(At.Head, nameof(Save))]
private Control BeforeSave()
{
    return IsReadOnly ? Control.Cancel : Control.Continue;
}
```

`Control.Cancel` skips the target body. `Control.Continue` lets it run. Concord combines cancel results with OR, so a later `Control.Continue` cannot undo an earlier cancellation.

Only Head injections can return `Control`. Concord reports `CONC015` at other positions.

A Head injection can take `ControlHandle` and call `Cancel()` instead. Use `ControlHandle<T>` when the target returns a value:

```csharp
[Inject(At.Tail, nameof(GetPrice))]
private void AfterGetPrice(ControlHandle<int> ch)
{
    ch.ReturnValue += 5;
}
```

The generic type must match the target's return type. Return and Tail injections can read or replace `ReturnValue`. Concord lowers each control handle into wrapper locals, so a target call does not allocate a handle object.

## Around in detail

A whole-method Around injection wraps the entire target body. The injection method declares an `Operation` family parameter and calls `original.Invoke(...)` to run the target body:

```csharp
[Patch]
abstract class LoadPatch : SaveSystem
{
    [Inject(At.Around, nameof(LoadObject))]
    private object WrapLoad(string path, Operation<string, object> original)
    {
        Logger.Info($"Loading {path}");
        object result = original.Invoke(path);
        Logger.Info("Loaded.");
        return result;
    }
}
```

`original` matches the target method's parameter and return types, following the same `Operation`/`VoidOperation` table used by Invoke with `At.Around` (see [The Operation family](#the-operation-family)). Code before `original.Invoke(...)` runs first, and code after it sees the result.

`original.Invoke(...)` can pass changed arguments; the target body runs with whatever values you pass, not the injection's own parameters. Leaving out the call skips the target body entirely and uses the injection's return value instead. Calling `original.Invoke(...)` from more than one distinct call site runs the target body more than once.

Whole-method Around targets one method with one handle: it does not accept a `ControlHandle`. It rejects targets with `ref`/`out`/pointer parameters or a `ref` return, and async or iterator methods.

One whole-method Around injection can target a method. A second one fails with `CONC051`. Head, Return, and Tail injections can compose with a whole-method Around on the same target; only call-site Invoke, Argument, and Constant injections on that target are rejected (`CONC115`), because there is no longer a single call site to match against.

## Invoke in detail

An Invoke injection changes one call inside the target method. This target calls `PriceRules.ApplyMarkup(int)`:

```csharp
public int GetFinalPrice(int basePrice)
{
    int markedUp = PriceRules.ApplyMarkup(basePrice);
    return markedUp + ShippingCost;
}
```

Invoke with `At.Around` can receive the call's arguments and an `Operation` handle:

```csharp
[Inject(nameof(GetFinalPrice), typeof(PriceRules), nameof(PriceRules.ApplyMarkup), At.Around)]
private int AroundApplyMarkup(int basePrice, Operation<int, int> original)
{
    Logger.Info("Applying markup.");
    int markedUp = original.Invoke(basePrice - 5);
    Logger.Info($"Marked up price: {markedUp}");
    return markedUp;
}
```

Code before `original.Invoke(...)` runs before `ApplyMarkup`. Code after it sees the call's result. Passing a new value changes the argument. Leaving out `Invoke` skips `ApplyMarkup` and uses the injection's return value as the call result:

```csharp
[Inject(nameof(GetFinalPrice), typeof(PriceRules), nameof(PriceRules.ApplyMarkup), At.Around)]
private int ReplaceApplyMarkup(int basePrice, Operation<int, int> original)
{
    return 20;
}
```

The Invoke form of `[Inject]` names the target method, the call owner's type, the called method, and the shift. Use `At.Head` to run before the call. `At.Tail` runs after the call but does not expose its result. Use `At.Around` when the injection needs that result. `At.Argument` replaces one argument without wrapping the call.

Use `by` when the body contains more than one matching call. The value counts from 1. The default, `by: 0`, matches all of them. Concord reports `CONC031` when it cannot find the requested call. It reports `CONC033` when `by` is larger than the number of matches.

### The Operation family

The `Operation` type must match the called method's arguments and return type:

| Called method | Handle type |
| --- | --- |
| No arguments, returns `void` | `Operation` |
| No arguments, returns `TResult` | `Operation<TResult>` |
| One argument, returns `void` | `VoidOperation<T1>` |
| One argument, returns `TResult` | `Operation<T1, TResult>` |
| Two arguments, returns `void` | `VoidOperation<T1, T2>` |
| Two arguments, returns `TResult` | `Operation<T1, T2, TResult>` |
| Three arguments, returns `TResult` | `Operation<T1, T2, T3, TResult>` |

Argument types come first. The result type comes last. `Invoke(...)` takes the called method's argument types and returns its result type.

Invoke with `At.Around` supports up to eight arguments for calls that return a value, and up to eight arguments for `void` calls. Concord does not support calls on value-type receivers. It reports `CONC039` for an unsupported or mismatched call.

### Argument evaluation

Consider a call with a computed argument:

```csharp
PriceRules.ApplyMarkup(basePrice - discount)
```

The wrapper evaluates `basePrice - discount` once and stores the value in a local before the injection runs. The injection can receive that value as its first parameter. Skipping `Invoke` skips `ApplyMarkup` and its side effects, but the argument expression has run by that point.

## Other targets inside a method

| Target | Concord support |
| --- | --- |
| Method call | Use Invoke with `At.Head`, `At.Tail`, or `At.Around` |
| Property getter or setter | Match the property name when it has one accessor. Use `get_Name` or `set_Name` when it has both. An Around injection can infer the accessor from its `Operation` type. |
| Inlined `int`, `long`, `float`, `double`, or `string` literal | Use `At.Constant` |
| One argument of a matched call | Use `At.Argument` |
| Field read or write | Concord has no instruction matcher yet. Use `[InjectField]` to access the field from another injection. |
| Object construction | The roadmap lists a constructor-call matcher. Patching the body of a constructor works now. |
| Local, branch, or raw return instruction | The roadmap lists a low-level IL API. |

`At.Constant` matches compiler output. The planned field, constructor-call, and low-level IL matchers would do the same. A source change can move a literal, renumber locals, or rewrite branch instructions. Check these patches again after the target changes. See the [Roadmap](roadmap.md#more-injection-positions) for the planned matchers.

## Ordering patches on one target

Concord orders injections that target the same method. A rule on one target does not affect another target.

Without a before or after rule, `Priority` decides the order. Lower priorities run first. Concord uses apply sequence to break a tie, so the injection applied later runs first when both priorities match.

For a target that returns `5`, a `+1` Tail injection at priority 1 and a `*2` Tail injection at priority 2 produce `(5 + 1) * 2`. Applying or removing a patch makes Concord rebuild the wrapper in the new order.

Use a type when your project references the other patch:

```csharp
[Patch]
[PatchBefore(typeof(BasePricePatch))]
abstract class DiscountPatch : ShopItem
{
    [Inject(At.Tail, nameof(GetPrice))]
    void ApplyDiscount(ControlHandle<int> ch)
    {
        ch.ReturnValue -= 2;
    }
}
```

`DiscountPatch` runs before each live injection owned by `BasePricePatch` on `GetPrice`. The type form uses the full name of the patch declaration as its owner.

Use a string when the other patch comes from an assembly that your project does not reference:

```csharp
[Patch]
[PatchAfter("OptionalPrices.SeasonalPricePatch")]
abstract class PricePatch : ShopItem
{
    [Inject(At.Tail, nameof(GetPrice))]
    void AdjustPrice(ControlHandle<int> ch)
    {
        ch.ReturnValue += 5;
    }
}
```

Owner names are case-sensitive. A missing owner adds no ordering edge. Concord keeps the rule and uses it when that owner patches the same target. Removing the owner removes the edge.

You can add more than one `[PatchBefore]` or `[PatchAfter]` attribute. Repeating the same rule has no extra effect. An explicit rule wins over `Priority`. A cycle fails with `CONC052`, and Concord leaves the installed wrapper unchanged.

Before and after describe the order in which injection code starts. If Around injections nest at one site, the patch marked before runs on the outside. Its code before the nested call runs first, and its code after the call runs last. A whole-method Around still targets its method alone; only one can be applied at a time, though Head, Return, and Tail injections can compose alongside it.

## Calling the original body

Whole-method Around and Invoke with `At.Around` provide their own way to run the original code. Other code can use `ReversePatchFactory.Bind` to clone the original body into a delegate:

```csharp
MethodBase getPrice = typeof(ShopItem).GetMethod(nameof(ShopItem.GetPrice))!;

var original = (Func<ShopItem, int>)ReversePatchFactory.Bind(
    getPrice,
    typeof(Func<ShopItem, int>));

int originalPrice = original(item);
```

The delegate bypasses all patches on `GetPrice`, including patches from the assembly that created it.
