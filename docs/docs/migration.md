# Migrating from Harmony and Prepatcher

If you already write Harmony patches or Prepatcher fields, you know what you want to do. This page shows the Harmony or Prepatcher form first, followed by Concord's declarative and imperative forms. Where Concord has no match yet, the page says so and points at the [roadmap](roadmap.md).

## From Harmony

### The anatomy of a patch

A common Harmony patch uses a static container class. Attributes name the target, method names (`Prefix`, `Postfix`) pick the position, and parameter names (`__instance`, `__result`, `___field`) tell Harmony what to inject:

```csharp
[HarmonyPatch(typeof(ShopItem), nameof(ShopItem.GetPrice))]
static class ShopItem_GetPrice_Patch
{
    static void Postfix(ShopItem __instance, ref int __result)
    {
        __result += 5;
    }
}
```

The declarative Concord form extends the target type instead. There are no reserved parameter names. `this` is the instance, target members bind through ordinary C#, and the return value comes through a `ControlHandle<T>`:

```csharp
[Patch]
abstract class PricePatch : ShopItem
{
    [Inject(At.Tail, nameof(GetPrice))]
    void AfterGetPrice(ControlHandle<int> ch)
    {
        ch.ReturnValue += 5;
    }
}
```

The imperative form keeps the injection body in a helper type. `Patcher.For` selects the target, `Tail` selects the position and injection method, and `Apply` installs it:

```csharp
abstract class PriceInjections : ShopItem
{
    public void AfterGetPrice(ControlHandle<int> ch)
    {
        ch.ReturnValue += 5;
    }
}

IPatchHandle handle = Patcher.For<ShopItem>(nameof(ShopItem.GetPrice))
    .Tail(typeof(PriceInjections), nameof(PriceInjections.AfterGetPrice))
    .Apply();
```

Applying is one call, with no per-mod id string:

```csharp
// Harmony
var harmony = new Harmony("me.mymod");
harmony.PatchAll();

// Concord, declarative
IPatchHandle handle = Patcher.Apply(typeof(PricePatch).Assembly);
```

When the project references `Concord.Generators`, `Patcher.Apply` reads its generated patch registry and applies every `[Patch]` declaration together. If the assembly has no generated registry, Concord falls back to a reflection scan. The imperative builder applies only the injections added to that builder. Both forms return an `IPatchHandle`.

Choose one registration form for each injection. Do not scan a declarative patch with `Patcher.Apply` and register the same method through `Patcher.For`. The shorter imperative examples below point at the declaration method shown above them. In imperative-only code, remove `[Inject]`. The `[Patch]` marker is optional, but keeping it lets the analyzers check the target and its member mappings. Attributes such as `[InjectField]` stay on the helper type.

Harmony unpatches by id (`harmony.UnpatchAll("me.mymod")`). Concord unpatches either form by disposing its handle: `handle.Dispose()`.

One habit carries over unchanged: several injection methods can live in one declaration, the way several patch methods live in one Harmony class.

### Quick reference

| Harmony | Concord |
| --- | --- |
| `[HarmonyPatch(typeof(T))]` container class | `[Patch]` declaration extending `T`, or `[Patch(typeof(T))]` when `T` is sealed or static |
| `Prefix` | `[Inject(At.Head, ...)]` |
| `Postfix` | `[Inject(At.Return, ...)]` for each normal return; `At.Tail` for the last return only. Neither runs when a Concord Head cancels the target |
| `return false` from a prefix | `return Control.Cancel` (or `ch.Cancel()`) |
| `ref int __result` | `ControlHandle<int>` and `ch.ReturnValue` |
| `__instance` | `this`, when the declaration extends the target; `[InjectInstance]` when it can't |
| `___privateField` | `[InjectField("privateField")]` on a typed declaration field |
| `__state` between prefix and postfix | no direct equivalent; see [Migrate Harmony `__state`](#migrate-harmony-__state) |
| `ref` argument rewriting in a prefix | declare the parameter and assign it |
| Transpiler that wraps a call, changes its arguments, or replaces it | invoke injection with the `Operation` family |
| Transpiler that edits a constant | `At.Constant` |
| Transpiler that edits fields or branches | no analog yet ([roadmap](roadmap.md#more-injection-positions)) |
| Finalizer | `try`/`catch` around the splice call in an `At.Around` injection |
| `[HarmonyReversePatch]` | `ReversePatchFactory.Bind` |
| `AccessTools` / `Traverse` | shadow fields, `[InjectField]`, `[InjectProperty]`, `[InjectMethod]` |
| `MethodType.Getter` / `MethodType.Setter` | use `nameof(T.X)` when the property has one accessor; otherwise target `"get_X"` or `"set_X"` |
| `MethodType.Constructor` | `[Inject(At.Head)]` with no method name |
| `MethodType.StaticConstructor` | not supported through Concord's author API |
| `MethodType.Enumerator` or manual async state-machine targeting | not supported through Concord's author API yet |
| `argumentTypes` | `parameterTypes: [...]`, or `targetParameterTypes:` on the invoke attribute form |
| `[HarmonyPriority]` | choose a new `Priority = n` based on Concord's ordering; do not copy Harmony's number |
| `[HarmonyBefore]` / `[HarmonyAfter]` | `[PatchBefore(...)]` / `[PatchAfter(...)]` with a patch type or owner string |
| `harmony.PatchAll()` | `Patcher.Apply(assembly)` |
| `harmony.Patch(original, ...)` | `Patcher.For(...).Head(...).Apply()` |
| `harmony.UnpatchAll(id)` | `IPatchHandle.Dispose()` |

Imperative registration uses the same injection body. Pick the builder method that matches the patch:

| Patch role | Imperative method |
| --- | --- |
| Prefix | `Patcher.For(...).Head(...).Apply()` |
| Postfix at every normal return | `Patcher.For(...).Return(...).Apply()` |
| Postfix at the last return | `Patcher.For(...).Tail(...).Apply()` |
| Whole-method wrapper or finalizer replacement | `Patcher.For(...).Around(...).Apply()` |
| Wrap or replace a method call | `Patcher.For(...).Invoke(...).Apply()` |
| Constructor prefix | `Patcher.ForConstructor(...).Head(...).Apply()` |
| Custom position or priority | `Patcher.PatchInjection(...)` |

Each call returns an `IPatchHandle`. Dispose that handle to remove the patch.

### Skip the original

A Harmony prefix skips the original by returning `false`:

```csharp
static bool Prefix(Door __instance)
{
    return !__instance.IsLocked;
}
```

In Concord the skip is explicit. Return `Control.Cancel` from a head injection:

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

Register the same head injection through the imperative API:

```csharp
IPatchHandle handle = Patcher.For<Door>(nameof(Door.Open))
    .Head(typeof(DoorPatch), "BeforeOpen")
    .Apply();
```

This is almost the Harmony prefix, but the decision has a name instead of a bare bool: `Control.Cancel` skips, `Control.Continue` runs. You never have to remember which way `false` points. An injection that already takes a `ControlHandle` (say, to set `ReturnValue`) can call `ch.Cancel()` instead.

One difference to remember: when Harmony skips a non-void method without setting `__result`, the caller silently gets `default`. Concord refuses to compose that patch at all. Cancelling a non-void target without setting `ch.ReturnValue` fails with `CONC012`.

### Read or replace the return value

`ref __result` becomes `ControlHandle<T>`:

```csharp
// Harmony
static void Postfix(ref int __result)
{
    __result = 1;
}

// Concord, declarative
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

The imperative form registers that method with `Tail`:

```csharp
IPatchHandle handle = Patcher.For<ShopItem>(nameof(ShopItem.GetPrice))
    .Tail(typeof(PricePatch), "AfterGetPrice")
    .Apply();
```

A Harmony postfix runs after each normal completion. Concord's `At.Return` fires before every `return` site of a multi-exit method and can adjust the value where it is produced. `At.Tail` is narrower: it fires only before the last `return`, matching Mixin's `@At("TAIL")`.

There is one control-flow difference. Harmony still runs postfixes after a prefix skips the original. A Concord Head cancellation branches past the target body, so its Return and Tail injections do not run.

### The instance and private members

`__instance` disappears: the declaration extends the target, so the instance is `this` and public or protected members bind on their own. `___fieldName` becomes an injected field, a typed declaration that maps to the target's private field:

```csharp
// Harmony
static void Postfix(GameActor __instance, ref int ___hitPoints)
{
    if (___hitPoints < 1)
    {
        ___hitPoints = 1;
    }
}

// Concord
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

The imperative builder can use the same helper type and injected field mapping:

```csharp
IPatchHandle handle = Patcher.For<GameActor>(nameof(GameActor.TakeDamage))
    .Tail(typeof(HealthPatch), "AfterTakeDamage")
    .Apply();
```

The declared type and static form must match the target field. On a declarative `[Patch]` type, `Concord.Analyzers` checks the mapping during the build when it can resolve the target type. An imperative-only helper is checked when Concord applies the patch.

Both of these forms lean on the declaration extending the target. When it can't, the next section has the mapping.

### When the declaration can't extend the target

Harmony never subclasses anything, so sealed targets cost it nothing. The extending form above stops working, though, when the target is `sealed`, `static`, has no constructor you can reach, or is a type you can only name by string. Harmony code on a sealed target looks like every other Harmony patch, usually with `Traverse` or `AccessTools` filling the private-member gaps:

```csharp
[HarmonyPatch(typeof(SealedFurnace), nameof(SealedFurnace.Tick))]
static class SealedFurnace_Tick_Patch
{
    static void Postfix(SealedFurnace __instance, ref int ___fuel)
    {
        ___fuel -= 1;
        Traverse.Create(__instance).Property("Mood").SetValue(0);
        AccessTools.Method(typeof(SealedFurnace), "Recalculate").Invoke(__instance, new object[] { 5 });
        Logger.Info(__instance.ToString());
    }
}
```

In Concord, name the target on the attribute instead of extending it, and declare a stand-in for each member you need. The declarations are typed, so the reflection soup disappears:

```csharp
[Patch(typeof(SealedFurnace))]
abstract class FurnacePatch
{
    [InjectInstance]
    protected abstract SealedFurnace Self { get; }

    [InjectField("fuel")]
    private int fuel;

    [InjectProperty("Mood")]
    protected abstract int Mood { get; set; }

    [InjectMethod("Recalculate")]
    protected abstract int Recalculate(int add);

    [Inject(At.Tail, nameof(SealedFurnace.Tick))]
    void AfterTick()
    {
        fuel -= 1;
        Mood = 0;
        Recalculate(5);
        Logger.Info(Self.ToString());
    }
}
```

For imperative registration, keep the injected-member attributes, omit `[Patch]` and `[Inject]`, and pass the helper method to the builder:

```csharp
IPatchHandle handle = Patcher.For<SealedFurnace>(nameof(SealedFurnace.Tick))
    .Tail(typeof(FurnacePatch), "AfterTick")
    .Apply();
```

Each Harmony convention has a direct counterpart:

| Harmony | Concord declaration |
| --- | --- |
| `__instance` | `[InjectInstance]` on an abstract get-only property; use it wherever the extending form would use `this` |
| `___fuel` | `[InjectField("fuel")]` on a typed declaration field |
| `Traverse.Create(__instance).Property("Mood")` | `[InjectProperty("Mood")]` on an abstract property with the accessors you need |
| `AccessTools.Method(typeof(T), "Recalculate").Invoke(...)` | `[InjectMethod("Recalculate")]` on an abstract method with the target's exact signature |

For a declarative patch, `Concord.Generators` can generate the field, property, and method declarations in this table. Add `[Shadow("memberName")]` to an abstract partial patch class, then use the generated member through `this`. The [private member generation example](common-tasks.md#generate-private-member-declarations) shows the full form.

A few rules carry the weight Harmony's runtime lookups used to. The declaration's type and signature must match the target member exactly; a mismatch fails with `CONC072`, a missing member with `CONC071`, and an ambiguous one with `CONC073`. Unlike `___`-prefixed parameters, the declared name doesn't have to mirror the target's: `[InjectField("_fuel")] private int fuel;` maps a clean local name onto an ugly private one. For declarative patches, `Concord.Analyzers` checks these mappings at build time when it can resolve the target type. Imperative-only helpers get the same mapping checks when the patch is applied.

The same pattern covers the other non-extendable cases. A `static` target class takes a `static` declaration class with static injection methods, minus `[InjectInstance]`, since there's no instance to inject (asking for one on a static target fails with `CONC074`). A type you can't reference at compile time takes `[Patch("Some.Internal.TypeName")]` with the members declared the same way. [How patches work](how-patches-work.md#sealed-and-non-subclassable-targets) covers the mechanics.

### Rewrite an argument

A Harmony prefix takes the parameter by `ref`:

```csharp
static void Prefix(ref int amount)
{
    if (amount > 100)
    {
        amount = 100;
    }
}
```

A Concord injection declares the target's parameter and assigns it. You don't need `ref`. The injection's parameters are the target's arguments, not copies, so the original body runs with the value you wrote:

```csharp
[Patch]
abstract class CampfirePatch : Campfire
{
    [Inject(At.Head, nameof(BurnFuel))]
    void BeforeBurnFuel(int amount)
    {
        if (amount > 100)
        {
            amount = 100;
        }
    }
}
```

The imperative form selects the same target and head method directly:

```csharp
IPatchHandle handle = Patcher.For<Campfire>(nameof(Campfire.BurnFuel))
    .Head(typeof(CampfirePatch), "BeforeBurnFuel")
    .Apply();
```

### Migrate Harmony `__state`

Harmony gives each patch class a separate [`__state`](https://harmony.pardeike.net/articles/patching-prefix.html#passing-state-between-prefix-and-postfix) value for each call to the patched method. The prefix writes it through an `out` or `ref` parameter. The postfix receives the stored value through its own `__state` parameter. Both methods must be in the same patch class because Harmony matches `__state` by the declaring type. When a patch carries several pieces of data, the prefix stores them in one object of a custom type. The postfix receives that same object.

```csharp
[HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.Load))]
static class LoadPatch
{
    static void Prefix(out Stopwatch __state)
    {
        __state = Stopwatch.StartNew();
    }

    static void Postfix(Stopwatch __state)
    {
        __state.Stop();
        Logger.Info($"Load took {__state.Elapsed}");
    }
}
```

Concord has no direct `__state` equivalent. There is no per-call state channel between a `Head` injection and a `Tail` or `Return` injection. Combine the prefix and postfix into one whole-method `At.Around` injection and keep the state in a local variable:

```csharp
[Patch]
abstract class LoadPatch : SaveSystem
{
    [Inject(At.Around, nameof(Load))]
    object MeasureLoad(string path, Operation<string, object> original)
    {
        Stopwatch state = Stopwatch.StartNew();
        object result = original.Invoke(path);
        state.Stop();
        Logger.Info($"Load took {state.Elapsed}");
        return result;
    }
}
```

Register the Around injection imperatively when you do not want assembly scanning:

```csharp
IPatchHandle handle = Patcher.For<SaveSystem>(nameof(SaveSystem.Load))
    .Around(typeof(LoadPatch), "MeasureLoad")
    .Apply();
```

`original.Invoke(path)` runs the original method body. The local variable remains in scope before and after that call, and each call gets its own value.

Concord accepts one whole-method `Around` injection per target and reports `CONC051` for a second. Head, Return, and Tail injections can still compose alongside it. Do not store per-call state in a patch field; recursion or concurrent calls can overwrite it.

### Patch a property

Harmony selects accessors with `MethodType`:

```csharp
[HarmonyPatch(typeof(ShopItem), nameof(ShopItem.Price), MethodType.Getter)]
```

Concord accepts the property name when it has only one accessor. If `ShopItem.Price` is read-only, this targets its getter:

```csharp
[Patch]
abstract class PricePatch : ShopItem
{
    [Inject(At.Tail, nameof(ShopItem.Price))]
    void AfterGetPrice(ControlHandle<int> ch)
    {
        ch.ReturnValue += 5;
    }
}
```

The imperative selector uses the getter's method name:

```csharp
IPatchHandle handle = Patcher.For<ShopItem>("get_Price")
    .Tail(typeof(PricePatch), "AfterGetPrice")
    .Apply();
```

The exact accessor name works too. It is required for a whole-method `[Inject]` when the property has both a getter and a setter:

```csharp
[Patch]
abstract class PricePatch : ShopItem
{
    [Inject(At.Tail, "get_Price")]
    void AfterGetPrice(ControlHandle<int> ch)
    {
        ch.ReturnValue += 5;
    }

    [Inject(At.Head, "set_Price")]
    void BeforeSetPrice(int value, ControlHandle ch)
    {
        if (value < 0)
        {
            ch.Cancel();
        }
    }
}
```

Getter and setter patches need separate builders because they target separate methods:

```csharp
IPatchHandle getter = Patcher.For<ShopItem>("get_Price")
    .Tail(typeof(PricePatch), "AfterGetPrice")
    .Apply();

IPatchHandle setter = Patcher.For<ShopItem>("set_Price")
    .Head(typeof(PricePatch), "BeforeSetPrice")
    .Apply();
```

Invoke matching follows the same rule, with one extra clue. An Around invoke can use its handle type to select the accessor. `Operation<int>` fits a zero-argument getter that returns `int`; `VoidOperation<int>` fits a setter that takes one `int`. In those cases, `nameof(Supplier.BasePrice)` works even when the property has both accessors. The exact names `"get_BasePrice"` and `"set_BasePrice"` always work.

This property-name resolution applies to `[Inject]` targets and method names matched by `Invoke`. The direct fluent selector `Patcher.For(...)` still expects a method name, so use `"get_Price"` or `"set_Price"` there.

### Patch a constructor

`MethodType.Constructor` becomes an `[Inject]` with no method name, and `argumentTypes` becomes `parameterTypes`:

```csharp
// Harmony
[HarmonyPatch(typeof(GameActor), MethodType.Constructor, new[] { typeof(FactionId) })]

// Concord, declarative
[Patch]
abstract class ActorConstructionPatch : GameActor
{
    [Inject(At.Head, parameterTypes: [typeof(FactionId)])]
    public void OnConstruct(ControlHandle ch) { }
}
```

The imperative API selects a constructor with `Patcher.ForConstructor`:

```csharp
IPatchHandle handle = Patcher.ForConstructor<GameActor>([typeof(FactionId)])
    .Head(typeof(ActorConstructionPatch), nameof(ActorConstructionPatch.OnConstruct))
    .Apply();
```

Static constructors are the exception. Harmony can target a `.cctor`, though it has often run before the patch is applied. Concord's author API has no static-constructor target form. Details are in [Common Tasks](common-tasks.md#patch-a-constructor).

### Pick an overload

Same idea, different spelling. `argumentTypes` in the `[HarmonyPatch]` attribute becomes `parameterTypes` on `[Inject]`:

```csharp
[Patch]
abstract class StackPatch : ItemStack
{
    [Inject(At.Head, nameof(Add), parameterTypes: [typeof(int)])]
    public void BeforeAdd(int count) { }
}
```

The imperative target selector takes the same parameter-type array:

```csharp
IPatchHandle handle = Patcher.For<ItemStack>(nameof(ItemStack.Add), [typeof(int)])
    .Head(typeof(StackPatch), nameof(StackPatch.BeforeAdd))
    .Apply();
```

An ambiguous target name is rejected during the patch scan. Concord reports the declaration error and skips that declaration. Pass the parameter types to select one overload.

### Transpilers

Use an invoke injection when a Harmony transpiler targets a supported method call. Its attribute identifies both methods: the one you want to patch and the call inside it:

```csharp
[Patch]
abstract class PricePatch : ShopItem
{
    [Inject(nameof(GetFinalPrice), typeof(PriceRules), nameof(PriceRules.ApplyMarkup), At.Around)]
    int AroundApplyMarkup(int basePrice, Operation<int, int> original)
    {
        if (UseFlatPrice)
        {
            return 20;              // replace the call
        }

        return original.Invoke(basePrice - 5);   // or change its argument
    }
}
```

The imperative builder selects the outer target first, then the call inside it:

```csharp
IPatchHandle handle = Patcher.For<ShopItem>(nameof(ShopItem.GetFinalPrice))
    .Invoke(
        typeof(PriceRules),
        nameof(PriceRules.ApplyMarkup),
        typeof(PricePatch),
        "AroundApplyMarkup",
        At.Around)
    .Apply();
```

`GetFinalPrice` is the target method, and `PriceRules.ApplyMarkup(int)` is the call inside it. The `Operation<int, int>` parameter represents that call. The first `int` gives the argument type, and the second gives the return type.

Around invoke supports up to eight arguments for a call that returns a value, and up to eight for a `void` call. Calls on value-type receivers are not supported. Concord reports `CONC039` when a call does not fit those limits.

`original.Invoke(basePrice - 5)` calls `ApplyMarkup` with a changed argument. The other branch returns `20` without calling `ApplyMarkup`, so `20` becomes the call's result. Put code before `Invoke` to run it before `ApplyMarkup`, or after `Invoke` to inspect the return value.

Add `by` to choose one call when the target contains several matches. Concord reads it as a 1-based ordinal. The default, `by: 0`, patches every match. A Harmony transpiler would make the same choice by scanning `CodeInstruction` objects and counting opcodes. [Common Tasks](common-tasks.md#wrap-a-call-inside-the-target) covers each form.

Pass `by` to the builder's `Invoke` method the same way. If the called method is overloaded, use the overload that accepts its parameter types:

```csharp
IPatchHandle handle = Patcher.For<ShopItem>(nameof(ShopItem.GetFinalPrice))
    .Invoke(
        typeof(PriceRules),
        nameof(PriceRules.ApplyMarkup),
        [typeof(int)],
        typeof(PricePatch),
        "AroundApplyMarkup",
        At.Around,
        by: 1)
    .Apply();
```

Use `At.Constant` when a transpiler replaces an `int`, `long`, `float`, `double`, or `string` literal. The fluent builder has no `Constant` method, so imperative code uses `Patcher.PatchInjection`. This low-level form uses `System.Reflection` and `Concord.Emit`:

```csharp
static class AgeGateInjections
{
    public static float RaiseMinimumAge(float original) => 20f;
}

MethodInfo method = typeof(AgeGateInjections).GetMethod(
    nameof(AgeGateInjections.RaiseMinimumAge))!;

Injection injection = new Injection(
    method,
    new InjectAt.Constant(18f),
    Owner: "me.mymod",
    Priority: 0);

IPatchHandle handle = Patcher.PatchInjection(
    typeof(AgeGate).GetMethod(nameof(AgeGate.Allows))!,
    injection);
```

The replacement method must take and return the literal's type. See [Replace a constant](common-tasks.md#replace-a-constant) for the declarative form.

Concord has no instruction stream for other IL edits because opcode positions can move when the target changes. Head and Tail invoke injections can run before or after a field read, but they cannot replace that value. Keep a patch on Harmony if it redirects a field load, writes a field, rewrites branches, or makes another edit that Concord's positions cannot express. Ordinary C# locals inside an injection work. See the [roadmap](roadmap.md#more-injection-positions) for lower-level IL editing.

### Call the unpatched original

`[HarmonyReversePatch]` becomes a factory call, no attribute or stub method needed:

```csharp
MethodBase getPrice = typeof(ShopItem).GetMethod(nameof(ShopItem.GetPrice))!;

var original = (Func<ShopItem, int>)ReversePatchFactory.Bind(getPrice, typeof(Func<ShopItem, int>));

int unpatched = original(item);
```

The delegate calls the original body directly, bypassing every patch on the method, yours included.

### Priorities and ordering

Concord also uses an integer priority:

```csharp
[Inject(At.Tail, nameof(GetPrice), Priority = 2)]
```

For a declarative patch, put relative ordering on the patch declaration. Use a type when the other patch is referenced, or its owner string when it is not:

```csharp
[Patch]
[PatchBefore(typeof(BasePricePatch))]
[PatchAfter("OptionalPrices.SeasonalPricePatch")]
abstract class PricePatch : ShopItem
{
    [Inject(At.Tail, nameof(GetPrice), Priority = 2)]
    void AfterGetPrice(ControlHandle<int> ch)
    {
        ch.ReturnValue += 5;
    }
}
```

You can repeat either attribute to name more owners. The type form uses the declaration type's full name. The string form must match the other injection's `Owner`, including capitalization.

Harmony's string names a Harmony instance ID. A declarative Concord patch uses its declaration type's full name instead. Do not copy the Harmony ID unless the other Concord injection uses that ID as its owner.

`PatchBuilder` uses priority `0`. An imperative patch with another priority or owner rule uses the low-level `Injection` API:

```csharp
MethodInfo method = typeof(PricePatch).GetMethod(
    "AfterGetPrice",
    BindingFlags.Instance | BindingFlags.NonPublic)!;

Injection injection = new Injection(
    method,
    new InjectAt.Tail(),
    Owner: "me.mymod",
    Priority: 2)
{
    BeforeOwners = [typeof(BasePricePatch).FullName!],
    AfterOwners = ["OptionalPrices.SeasonalPricePatch"],
};

IPatchHandle handle = Patcher.PatchInjection(
    typeof(ShopItem).GetMethod(nameof(ShopItem.GetPrice))!,
    injection);
```

A higher number runs later and further out in a tail chain: a `+3` patch at priority 1 and a `*2` patch at priority 2 on a method returning 4 compose to `(4 + 3) * 2`.

Do not copy a Harmony priority value directly. Harmony orders its patch types by different rules. Pick Concord values from the order you want.

An explicit before or after rule wins when it conflicts with `Priority`. Concord ignores a rule while its owner is missing and uses it when that owner patches the same target.

### Exceptions and finalizers

A Harmony finalizer runs when the target throws and can observe, swallow, or replace the exception. Concord has no dedicated on-throw position. Instead, wrap the target in an `At.Around` injection and write ordinary `try`/`catch` around the `original.Invoke(...)` call:

```csharp
[Patch]
abstract class LoadPatch : SaveSystem
{
    [Inject(At.Around, nameof(Load))]
    object WrapLoad(string path, Operation<string, object> original)
    {
        try
        {
            return original.Invoke(path);
        }
        catch (IOException e)
        {
            Logger.Warn($"Load failed: {e.Message}");
            return null;
        }
    }
}
```

The imperative form registers the same Around helper explicitly:

```csharp
IPatchHandle handle = Patcher.For<SaveSystem>(nameof(SaveSystem.Load))
    .Around(typeof(LoadPatch), "WrapLoad")
    .Apply();
```

The original body runs inside your `try`, so its exceptions land in your `catch`. Swallow by returning a fallback value, replace by throwing a different exception, or observe and rethrow with `throw;`. For the "always runs, even on throw" half of finalizer behavior, use `try`/`finally` the same way.

Concord accepts one whole-method Around per target; a second fails with `CONC051`. Head, Return, and Tail injections can still compose alongside it.

### Static, async, and generic targets

These target kinds need separate rules:

- **Static targets.** You can't extend a static class, so name the target explicitly, `[Patch(typeof(SaveSystem))]`, and make the declaration and injection methods static. Imperative code uses the type overload: `Patcher.For(typeof(SaveSystem), nameof(SaveSystem.Load)).Head(...).Apply()`.
- **Async methods and iterators.** Harmony can select an iterator's generated `MoveNext` with `MethodType.Enumerator`; async state machines need manual targeting. Concord's author API does not support either form yet. The low-level composer can resolve `MoveNext`, but the public apply path does not install that wrapper on the state machine correctly.
- **Generics.** Value-type instantiations compile to their own bodies, so patching `Box<int>.Get` affects `Box<int>` and nothing else. Reference-type instantiations are the trap: the runtime compiles one shared body for all of them, a detour lands on that shared body, and the wrapper composed for `Box<string>.Get` then runs for every other reference-type `Box<T>` too. Harmony has the same failure mode, silently. Concord rejects a reference-type instantiation at patch time with `CONC061`, so patch generic targets only at value-type instantiations.

### What the compiler now does for you

Harmony relies on names that the compiler cannot check. A misspelled `Prefix`, a stale `___field`, or a failed `AccessTools.Method` lookup may not surface near the mistake. Declarative Concord patches let the compiler bind target members, and `Concord.Analyzers` checks injection targets, signatures, and control-handle types.

Imperative helpers do not get those analyzer checks unless they also have `[Patch]`. `Patcher.For` and methods such as `Head` resolve their names at runtime, while signature errors surface when you apply the builder. Use `nameof` and parameter-type arrays where possible.

Runtime composition errors include a `CONCxxx` code listed in [Troubleshooting](troubleshooting.md). For low-level inspection, `WrapperComposer.ComposeDump` prints the wrapper Concord tried to build.

## From Prepatcher

Prepatcher can add real fields by rewriting a target assembly before normal game code runs. Concord changes live method behavior and does not change a type's layout. Code that used `[PrepatcherField]` therefore needs side storage instead of another real field.

### Injected fields become attached data

The Prepatcher accessor:

```csharp
[PrepatcherField]
public static extern ref int MyCounter(this GameActor target);
```

becomes an `AttachedField` keyed by the target instance. Add `using Concord.AttachedData;`:

```csharp
[Patch]
abstract class ActorExtensions : GameActor
{
    private static readonly AttachedField<GameActor, int> MyCounter = new();

    [Inject(At.Tail, nameof(TakeDamage))]
    private void AfterTakeDamage()
    {
        MyCounter.Set(this, MyCounter.Get(this) + 1);
    }
}
```

The same table also works outside an injection:

```csharp
MyCounter.Set(actor, MyCounter.Get(actor) + 1);
```

`Get` returns `default(TValue)` when nothing was stored. `TryGet` tells you whether an entry exists.

### A real field versus a side table

Don't treat the swap as one-to-one. The two models differ in ways that can matter:

| | Prepatcher injected field | Concord attached data |
| --- | --- | --- |
| Storage | a real field, added by assembly rewrite | a weak table keyed by instance |
| Access cost | plain field access | table lookup |
| Lifetime | collected with the instance | collected with the instance |
| Visible to | discoverable through target metadata and reflection | only code that can reach the `AttachedField` instance |
| Patch disposal | field remains on the rewritten type | stored entries are not cleared when a patch handle is disposed |
| Persistence | depends on the target runtime | memory-only in Core; a runtime adapter may add persistence |

An attached value needs a table lookup. Cache it in a local when one injection reads it several times. Concord cannot provide real-field access speed without changing the target type.

A Prepatcher field is visible through reflection on the target type. Attached data is visible only to code that can reach the table object.

### Default values and initializers

Prepatcher initializes injected fields declaratively:

```csharp
[PrepatcherField]
[DefaultValue(1)]
public static extern ref int MyCounter(this GameActor target);

[PrepatcherField]
[ValueInitializer(nameof(MakeObject))]
public static extern ref SomeObject MyObject(this GameActor target);
```

Concord has no initializer attributes. Unset attached data reads as `default(TValue)`.

For a constant default, set it in a constructor injection on the target:

```csharp
[Patch]
abstract class ActorExtensions : GameActor
{
    private static readonly AttachedField<GameActor, int> MyCounter = new();

    [Inject(At.Head)]
    private void OnConstruct()
    {
        MyCounter.Set(this, 1);
    }
}
```

For a factory value, initialize lazily where you first need it:

```csharp
if (!MyObject.TryGet(actor, out SomeObject value))
{
    value = new SomeObject(actor);
    MyObject.Set(actor, value);
}
```

### Component injection

`[InjectComponent]` caches a component that is already registered on the target. Concord has no matching helper. Use the runtime's component lookup or extension point, and cache the result in an `AttachedField` only when needed.

### Free patches

A `[FreePatch]` hands you the assembly as a Cecil `ModuleDefinition` to mutate freely. Split what yours does into two piles.

Behavior edits migrate when they fit a supported Concord position. Head, Tail, Return, Around, Invoke, Constant, and Argument cover many common changes. Arbitrary method-body edits do not.

Structural edits do not migrate. A detour cannot add real fields, implement another interface on an existing type, or change type metadata. [Enum patching](roadmap.md#enum-patching) is planned, but the other structural edits have no Concord equivalent.

## Where to go next

Start with [Your First Patch](first-patch.md) for a clean example. [Common Tasks](common-tasks.md) covers everyday patterns, [How patches work](how-patches-work.md) explains wrappers, and [Troubleshooting](troubleshooting.md) lists runtime diagnostic codes.
