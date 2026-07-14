# Common Tasks

The patches you'll use most often. Assumes you're comfortable reading C#.

Every patch declaration is marked with `[Patch]`. Most extend the target type. Use `[Patch(typeof(TargetType))]` or a string target when the declaration cannot inherit from it.

## Patch forms

The form of `[Inject]` decides what Concord matches. `At.*` decides where the injection runs within that form.

| Patch form | Attribute form | Fluent form |
| --- | --- | --- |
| Target method | `[Inject(At.Head, nameof(Method))]` | `.Head(...)`, `.Tail(...)`, `.Return(...)`, or `.Around(...)` |
| Call site | `[Inject(nameof(Method), typeof(Owner), nameof(Owner.Call), At.Around)]` | `.Invoke(...)` |
| Constant | `[Inject(nameof(Method), 5, At.Constant)]` | No high-level fluent form |
| Constructor body | `[Inject(At.Head)]` | `Patcher.ForConstructor(...)` |

The examples use attributes unless the fluent form makes target selection clearer. Reverse patches and injected members do not use an `At.*` position.

## Target-method injections

`[Inject(At.*, nameof(Method))]` patches the body of the named target method.

### `At.Head`

#### Run code before the target method

Use `At.Head`:

```csharp
[Patch]
abstract class DoorPatch : Door
{
    [Inject(At.Head, nameof(Open))]
    void BeforeOpen()
    {
        Logger.Info("A door is opening.");
    }
}
```

Good for logging, changing arguments or target members, and gating the method before its body runs. At runtime:

```csharp
public void Open()
{
    Logger.Info("A door is opening.");
    IsOpen = true;
}
```

#### Change a target method parameter

A Head injection can assign to a parameter with the same name and type as the target parameter:

```csharp
[Patch]
abstract class DamagePatch : GameActor
{
    [Inject(At.Head, nameof(TakeDamage))]
    void ClampDamage(int amount)
    {
        amount = Math.Max(0, amount);
    }
}
```

Concord maps `amount` to the target argument. The assignment changes the value read by the original body. At runtime:

```csharp
public void TakeDamage(int amount)
{
    amount = Math.Max(0, amount);
    hitPoints -= amount;
}
```

#### Stop the target method

Return `Control` from a head injection:

```csharp
[Patch]
abstract class DoorPatch : Door
{
    [Inject(At.Head, nameof(Open))]
    Control BeforeOpen()
    {
        return IsLocked ? Control.Cancel : Control.Continue;
    }
}
```

`Control.Cancel` skips the original method, and `Control.Continue` runs it. For a `void` target that's the whole story. If the method returns a value, you also need to set `ReturnValue` (next section).

An injection that already takes a `ControlHandle` can call `ch.Cancel()` instead. The two forms do the same thing, and a later patch can't undo either:

```csharp
[Inject(At.Head, nameof(Open))]
void BeforeOpen(ControlHandle ch)
{
    if (IsLocked)
    {
        ch.Cancel();
    }
}
```

At runtime:

```csharp
public void Open()
{
    if (IsLocked)
        return;

    IsOpen = true;
}
```

#### Cancel and return a value

For a non-void target, set `ReturnValue` before or when you cancel:

```csharp
[Patch]
abstract class PricePatch : ShopItem
{
    [Inject(At.Head, nameof(GetPrice))]
    Control BeforeGetPrice(ControlHandle<int> ch)
    {
        if (IsFree)
        {
            ch.ReturnValue = 0;
            return Control.Cancel;
        }

        return Control.Continue;
    }
}
```

If you cancel a non-void method without setting a return value, Concord reports `CONC012`. At runtime:

```csharp
public int GetPrice()
{
    if (IsFree)
        return 0;

    return BasePrice;
}
```

### `At.Tail`

#### Run code at the target method's last return

Use `At.Tail` for code that should run when execution reaches the last `return` in the method:

```csharp
[Patch]
abstract class DoorPatch : Door
{
    [Inject(At.Tail, nameof(Open))]
    void AfterOpen()
    {
        Logger.Info("The door opened.");
    }
}
```

An earlier return skips the Tail injection. At runtime:

```csharp
public void Open()
{
    if (IsLocked)
        return;

    IsOpen = true;
    Logger.Info("The door opened.");
}
```

#### Replace a return value

Use `ControlHandle<T>`, where `T` is the method's return type:

```csharp
[Patch]
abstract class PricePatch : ShopItem
{
    [Inject(At.Tail, nameof(GetPrice))]
    void AfterGetPrice(ControlHandle<int> ch)
    {
        ch.ReturnValue = 1;
    }
}
```

`At.Tail` runs before the method's last `return` and swaps the result to `1`. If the method has several `return` statements and you want to rewrite the value at each one (including early returns), use `At.Return` instead. At runtime:

```csharp
public int GetPrice()
{
    int result = BasePrice;
    result = 1;
    return result;
}
```

### `At.Return`

#### Run code at every return

Use `At.Return` when the target has several exits and each one needs the injection. `ControlHandle<T>` contains the value from the current return site:

```csharp
[Patch]
abstract class PricePatch : ShopItem
{
    [Inject(At.Return, nameof(GetPrice))]
    void ClampPrice(ControlHandle<int> ch)
    {
        ch.ReturnValue = Math.Max(0, ch.ReturnValue);
    }
}
```

`by: 0` (the default) targets every return. Pass `by: 2` to target only the second return in the method.

### `At.Around`

#### Wrap the whole method

`At.Around` with only a method name wraps the entire target. Your injection declares an `Operation` family parameter and calls `original.Invoke(...)` to run the target body. Everything before the call runs first, everything after runs last, and you can read or replace the result:

```csharp
[Patch]
abstract class LoadPatch : SaveSystem
{
    [Inject(At.Around, nameof(Load))]
    object WrapLoad(string path, Operation<string, object> original)
    {
        Logger.Info($"Loading {path}");
        object result = original.Invoke(path);
        Logger.Info("Loaded.");
        return result;
    }
}
```

`original` matches the target method's parameters and return type, following the same `Operation`/`VoidOperation` table as call-site Invoke with `At.Around`. `original.Invoke(...)` can pass changed arguments, and calling it from more than one call site runs the target body more than once. Leaving out the call skips the target body and uses the injection's return value instead.

At runtime, the wrapper behaves like this:

```csharp
public object Load(string path)
{
    Logger.Info($"Loading {path}");
    object result = /* original Load body */;
    Logger.Info("Loaded.");
    return result;
}
```

Only one whole-method Around can target a method; a second fails with `CONC051`. Head, Return, and Tail injections can compose alongside it; call-site Invoke, Argument, and Constant injections on that target are rejected (`CONC115`). For the full treatment, including `try`/`finally` and multiple returns, see [How patches work](how-patches-work.md#around-in-detail).

## Call-site injections

The invoke form matches a method or property call inside the target method. Its final `At.*` argument controls what happens at that call.

### `At.Head`

#### Run code before a call inside the target

Use the invoke form with `At.Head` to run code before a matched call:

```csharp
[Patch]
abstract class PricePatch : ShopItem
{
    [Inject(nameof(GetFinalPrice), typeof(PriceRules), nameof(PriceRules.ApplyMarkup), At.Head)]
    void BeforeApplyMarkup()
    {
        Logger.Info("Applying markup.");
    }
}
```

At runtime:

```csharp
public int GetFinalPrice(int basePrice)
{
    Logger.Info("Applying markup.");
    int markedUp = PriceRules.ApplyMarkup(basePrice);
    return markedUp + ShippingCost;
}
```

### `At.Around`

#### Wrap a call inside the target

`At.Around` can wrap a whole target method or one call inside it. The invoke form here wraps one matched call. Your injection gets an `Operation` handle shaped to that call: run it where you want, change its arguments, or skip it.

```csharp
[Patch]
abstract class PricePatch : ShopItem
{
    [Inject(nameof(GetFinalPrice), typeof(PriceRules), nameof(PriceRules.ApplyMarkup), At.Around)]
    int AroundApplyMarkup(int basePrice, Operation<int, int> original)
    {
        Logger.Info("Applying markup.");
        return original.Invoke(basePrice);
    }
}
```

The invoke form takes the call site's declaring type and method name, plus a `shift`. `At.Around` wraps the original call: your injection method gets an `Operation` handle shaped to the call's arguments and return type, and decides whether to call `original.Invoke(...)`. If it does not, the injection replaces the call. `PriceRules.ApplyMarkup` takes one `int` and returns `int`, so the handle here is `Operation<int, int>`; see [How patches work](how-patches-work.md#the-operation-family) for the full family. `At.Head` runs code before the call. To run code after it, use `At.Around` and place that code after `original.Invoke(...)`, as shown next. An optional `by` ordinal picks one matching call, counting from 1; leave it at `0` to match every call site.

Around invoke supports up to eight arguments for a call that returns a value, and up to eight for a `void` call. Calls on value-type receivers are not supported. Concord reports `CONC039` when a call does not fit those limits.

```csharp
public int GetFinalPrice(int basePrice)
{
    Logger.Info("Applying markup.");
    int markedUp = PriceRules.ApplyMarkup(basePrice);
    return markedUp + ShippingCost;
}
```

#### After a call inside the target

Since `At.Around` wraps the call, "after" is code after `original.Invoke(...)`:

```csharp
[Inject(nameof(GetFinalPrice), typeof(PriceRules), nameof(PriceRules.ApplyMarkup), At.Around)]
int AroundApplyMarkup(int basePrice, Operation<int, int> original)
{
    int markedUp = original.Invoke(basePrice);
    Logger.Info($"Marked up price: {markedUp}");
    return markedUp;
}
```

At runtime:

```csharp
public int GetFinalPrice(int basePrice)
{
    int markedUp = PriceRules.ApplyMarkup(basePrice);
    Logger.Info($"Marked up price: {markedUp}");
    return markedUp + ShippingCost;
}
```

#### Change a call's argument

Pass different arguments to `original.Invoke(...)`:

```csharp
[Inject(nameof(GetFinalPrice), typeof(PriceRules), nameof(PriceRules.ApplyMarkup), At.Around)]
int WrapApplyMarkup(int basePrice, Operation<int, int> original)
{
    int discountedBase = basePrice - 5;
    return original.Invoke(discountedBase);
}
```

At runtime:

```csharp
public int GetFinalPrice(int basePrice)
{
    int discountedBase = basePrice - 5;
    int markedUp = PriceRules.ApplyMarkup(discountedBase);
    return markedUp + ShippingCost;
}
```

#### Change several call arguments

An Around invoke can replace several arguments in one call. Pass each new value to `original.Invoke(...)`:

```csharp
[Patch]
abstract class PricePatch : ShopItem
{
    [Inject(nameof(GetSalePrice), typeof(PriceRules), nameof(PriceRules.Calculate), At.Around)]
    int AroundCalculate(
        int basePrice,
        int discount,
        bool taxable,
        Operation<int, int, bool, int> original)
    {
        int safePrice = Math.Max(0, basePrice);
        int cappedDiscount = Math.Min(discount, 50);
        return original.Invoke(safePrice, cappedDiscount, false);
    }
}
```

`original.Invoke(...)` sends all three replacement values to `PriceRules.Calculate(...)`. Use `At.Argument` when only one argument needs a replacement.

#### Replace a call entirely

Don't call `original.Invoke(...)`. Return your own value:

```csharp
[Inject(nameof(GetFinalPrice), typeof(PriceRules), nameof(PriceRules.ApplyMarkup), At.Around)]
int ReplaceApplyMarkup(int basePrice, Operation<int, int> original)
{
    if (UseFlatPrice)
    {
        return 20;
    }

    return original.Invoke(basePrice);
}
```

Be careful here. Skipping the original call means skipping its side effects too. At runtime:

```csharp
public int GetFinalPrice(int basePrice)
{
    int markedUp = UseFlatPrice
        ? 20
        : PriceRules.ApplyMarkup(basePrice);

    return markedUp + ShippingCost;
}
```

#### Wrap a value read

The invoke position also matches a property getter, since a getter is a method call underneath. Say `ShopItem.Total` reads `supplier.BasePrice`:

```csharp
public int Total()
{
    return supplier.BasePrice;
}
```

Target the property by name. Concord resolves it to the getter and shapes the `Operation<T>` handle to match:

```csharp
[Patch]
abstract class PricePatch : ShopItem
{
    [Inject(nameof(Total), typeof(Supplier), nameof(Supplier.BasePrice), At.Around)]
    int ShimBase(Operation<int> read)
    {
        return read.Invoke() - 2;
    }
}
```

At runtime:

```csharp
public int Total()
{
    int basePrice = supplier.BasePrice;
    return basePrice - 2;
}
```

A property with one accessor resolves from its property name, so `nameof(Supplier.BasePrice)` and the literal `"get_BasePrice"` reach the same call site in this example. If the property has both a getter and a setter, name the accessor directly (`"get_BasePrice"` or `"set_BasePrice"`) unless the injection's `Operation` shape disambiguates it.

### `At.Argument`

#### Rewrite a call argument

`At.Argument` rewrites one argument of a matched call without wrapping the whole call. The injection method takes and returns the argument's type:

```csharp
[Patch]
abstract class PricePatch : ShopItem
{
    [Inject(nameof(GetFinalPrice), typeof(PriceRules), nameof(PriceRules.ApplyMarkup), At.Argument, arg: 1)]
    int Clamp(int original)
    {
        return original > 10 ? 10 : original;
    }
}
```

`arg: 1` is 1-based and picks the first argument of the matched call. Leave `arg` at its default of `0` and Concord infers the argument by type, as long as exactly one parameter on the call site matches the injection method's parameter type:

```csharp
[Inject(nameof(GetFinalPrice), typeof(PriceRules), nameof(PriceRules.ApplyMarkup), At.Argument)]
int RaiseMinimum(int original)
{
    return original < 10 ? 10 : original;
}
```

If more than one argument shares that type, inference is ambiguous and composition fails with an error naming `arg:` so you know to pass it explicitly.

## Constant injections

`At.Constant` matches an inlined literal in a target method.

### `At.Constant`

#### Replace a constant

`At.Constant` targets an inlined literal in the target body instead of a call. It supports `int`, `long`, `float`, `double`, and `string` literals. The injection method takes and returns the constant's type:

```csharp
public class AgeGate
{
    public bool Allows(float age) => age >= 18f;

    public int AddTen(int value) => value + 5 + 5;
}

[Patch]
abstract class AgeGatePatch : AgeGate
{
    [Inject(nameof(Allows), 18f, At.Constant)]
    float RaiseMinimumAge(float original)
    {
        return 20f;
    }
}
```

This finds the literal `18f` inside `Allows` and replaces every occurrence with the injection's return value. Use `by` to narrow that down: `0` (the default) matches every occurrence, and a 1-based value picks a single one when the constant appears more than once:

```csharp
[Inject(nameof(AddTen), 5, At.Constant, by: 2)]
int BumpSecondFive(int original)
{
    return original + 1;
}
```

`by: 2` matches the second emitted `5` literal and leaves the first alone.

`At.Constant` anchors on compiler output, not source text. A constant match is only as stable as the IL the compiler happens to emit: a later source change can move the literal, fold it into a different constant, or drop it from the method entirely, and the injection stops matching. Treat it the way you'd treat any anchor on generated code, and re-check it after changing the target method.

## Constructor body injections

Constructor injections omit the target method name.

### `At.Head`

#### Patch a constructor

Drop the method name. An `[Inject]` with no method targets the declaring type's constructor:

```csharp
[Patch]
abstract class ActorConstructionPatch : GameActor
{
    [Inject(At.Head)]
    void OnConstruct(ControlHandle ch)
    {
        Logger.Info("A target actor is being constructed.");
    }
}
```

This runs at the head of `GameActor`'s parameterless constructor, before the original body. For an overloaded constructor, name the parameter types. The same `parameterTypes:` argument selects which one:

```csharp
[Inject(At.Head, parameterTypes: [typeof(FactionId)])]
void OnConstructWithFactionId(ControlHandle ch) { }
```

Or fluently:

```csharp
Patcher.ForConstructor<GameActor>([typeof(FactionId)])
    .Head(typeof(ActorConstructionPatch), "OnConstructWithFactionId")
    .Apply();
```

This works for instance constructors only. Static constructors (`.cctor`) run when a type is first touched, usually before a patch could apply, so Concord does not support them. A constructor body patch also cannot change which type gets built or skip the `new` operation. Constructor-call matching is planned but does not ship yet.

## Patch targets and member access

### Target one overload of a method

When the target method has overloads, the name alone is ambiguous. Pass the parameter types to pick one:

```csharp
[Patch]
abstract class StackPatch : ItemStack
{
    [Inject(At.Head, nameof(Add), parameterTypes: [typeof(int)])]
    void BeforeAddInt(ControlHandle ch)
    {
        Logger.Info("Adding by count.");
    }
}
```

This targets `Add(int)` and leaves `Add(Item)` untouched. Use `parameterTypes:` on ordinary and constant injections. On the invoke attribute form, use `targetParameterTypes:` for the outer target and `invokeParameterTypes:` for the matched call. The fluent target selector also accepts parameter types:

```csharp
Patcher.For<ItemStack>(nameof(ItemStack.Add), [typeof(int)])
    .Head(typeof(StackPatch), "BeforeAddInt")
    .Apply();
```

For an overloaded call-site method, pass parameter types to restrict the match. Without them, Concord matches every call instruction with that declaring type and method name. Pass the types to `PatchBuilder.Invoke(...)`, or use `invokeParameterTypes:` on `[Inject]`.

### Target an inaccessible nested type

Use a string target when C# cannot name the target type. CLR nested type names use `+` between the outer and inner type:

```csharp
[Patch("Game.Rendering.BlockRenderer+AmbientPass")]
abstract class AmbientPassPatch
{
    [Inject(At.Head, "Render")]
    void BeforeRender()
    {
        Logger.Info("Rendering the ambient pass.");
    }
}
```

Concord resolves the name from loaded assemblies. An assembly-qualified type name can disambiguate two assemblies that contain the same full name.

### Access the target instance

When the patch declaration extends its target, `this` is the current target object inside an injection:

```csharp
[Patch]
abstract class DoorPatch : Door
{
    [Inject(At.Head, nameof(Open))]
    void BeforeOpen()
    {
        Logger.Info($"Opening {this}.");
    }
}
```

If the declaration cannot extend the target, expose the same object with `[InjectInstance]`, as shown next.

### Read or write a private field

Declare a field with the same type and map it to the target field by name:

```csharp
[Patch]
abstract class HealthPatch : GameActor
{
    [InjectField("hitPoints")]
    private int hitPoints;

    [Inject(At.Tail, nameof(TakeDamage))]
    void AfterTakeDamage()
    {
        if (hitPoints < 1)
        {
            hitPoints = 1;
        }
    }
}
```

Use private-field access sparingly. Prefer public or protected members when the target already exposes what you need. At runtime:

```csharp
public void TakeDamage(int amount)
{
    hitPoints -= amount;

    if (hitPoints < 1)
        hitPoints = 1;
}
```

### Access members on a target you cannot inherit

Use an explicit target plus injected member declarations for a sealed target or any other type the patch declaration cannot extend:

```csharp
[Patch(typeof(SealedFurnace))]
abstract class FurnacePatch
{
    [InjectInstance]
    protected abstract SealedFurnace Self { get; }

    [InjectProperty("Temperature")]
    protected abstract int Temperature { get; set; }

    [InjectMethod("Recalculate")]
    protected abstract int Recalculate(int amount);

    [Inject(At.Tail, nameof(SealedFurnace.Tick))]
    void AfterTick()
    {
        Temperature = Math.Max(0, Temperature);
        Logger.Info($"{Self}: {Recalculate(5)}");
    }
}
```

`[InjectInstance]` exposes the current target object. `[InjectProperty]` and `[InjectMethod]` map the declarations to target members with matching types and signatures. `[InjectField]` does the same for a field, as shown in the previous section.

### Generate private member declarations

Projects that reference `Concord.Generators` can use `[Shadow]` instead of writing each injected member declaration. Mark the patch declaration `partial` so the generator can add the members:

```csharp
[Patch]
[Shadow("hitPoints")]
[Shadow("Recalculate", typeof(int))]
abstract partial class HealthPatch : GameActor
{
    [Inject(At.Tail, nameof(TakeDamage))]
    void AfterTakeDamage()
    {
        hitPoints = Recalculate(0);
    }
}
```

The parameter types on a method shadow select its overload. The generator emits typed `[InjectField]`, `[InjectProperty]`, or `[InjectMethod]` members after resolving the target at build time.

### Pick good patch declaration names

Patch declaration names should say what they change. `FreeStarterItemsPatch` is good. `Patch1` is not. Patch names show up in debugging and diagnostics.

## Reverse patches

### Call the unpatched original

To run the original from *inside a wrap*, you don't need this section: both a whole-method Around and an invoke Around call `original.Invoke(...)` on their `Operation` handle (see [Wrap the whole method](#wrap-the-whole-method)). Use a reverse patch when you need a standalone delegate to the original body from anywhere, bypassing *every* patch on the method. Use `ReversePatchFactory.Bind`:

```csharp
MethodBase getPrice = typeof(ShopItem).GetMethod(nameof(ShopItem.GetPrice))!;

var original = (Func<ShopItem, int>)ReversePatchFactory.Bind(getPrice, typeof(Func<ShopItem, int>));

int originalPrice = original(item);
```

Useful when an injection needs to compare patched behavior against the target's unmodified output. Most mods won't need reverse patches.

## Attached data

### Use attached data from a patch

Add `using Concord.AttachedData;`, then use `AttachedField<TTarget, TValue>` when a patch needs per-instance data that is not a real field on the target type:

```csharp
[Patch]
abstract class ActorExtensions : GameActor
{
    private static readonly AttachedField<GameActor, int> CustomHealth = new();

    [Inject(At.Tail, nameof(TakeDamage))]
    private void AfterTakeDamage()
    {
        if (CustomHealth.Get(this) < 0)
        {
            CustomHealth.Set(this, 0);
        }
    }
}
```

The static `AttachedField` owns a weak table keyed by each `GameActor`. Its values stay in memory while their target objects are alive. Core does not save them. See [Attached Data](attached-data.md) for `Get`, `Set`, `TryGet`, and persistence details.
