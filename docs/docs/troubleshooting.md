# Troubleshooting

Concord reports problems at two moments, and each moment has its own code prefix.

The analyzer package checks your patch declarations while you build. It reports a `CONCORDxxx`
diagnostic. The runtime checks the target method while it composes a wrapper. It throws a
`ConcordEmitException` with a `CONCxxx` code.

A build-time diagnostic is the better one to get. It names the line in your source. Fix those first.

## Analyzer diagnostics

Add the `Concord.Analyzers` package to get these. See [Packages](packages.md) for the reference.

An error stops the build. A warning does not, but each one names a patch that behaves in a way you
probably did not intend.

| Code | Severity | What it means |
| --- | --- | --- |
| `CONCORD001` | Error | A bootstrap assembly hard-references Concord, the runtime adapter, MonoMod, or Mono.Cecil. A bootstrap runs before those assemblies load. Use reflection, or move the code into the runtime adapter. |
| `CONCORD002` | Error | An `[InjectField]`, `[InjectProperty]`, or `[InjectMethod]` declaration names a member the target type does not have. |
| `CONCORD003` | Error | An injected member declaration found its target, but the type, static form, return type, or signature differs. |
| `CONCORD004` | Warning | The analyzer cannot resolve a string patch target, so it cannot check the declaration. Reference the target's project or assembly. |
| `CONCORD005` | Error | An `[Inject]` names a method or constructor the target type does not have. |
| `CONCORD006` | Error | The injection target name matches more than one overload. Pass `parameterTypes` to pick one. |
| `CONCORD007` | Error | An injection parameter does not bind to a target parameter by name and type, or a `ControlHandle<T>` does not match the target's return type. |
| `CONCORD008` | Error | A static target cannot use an instance declaration member or an injected target instance. |
| `CONCORD009` | Warning | A plain field on the declaration has the same name as a target field. Plain fields become attached data. Add `[InjectField]` if you meant to reach the target's field. |
| `CONCORD010` | Warning | Two injections declare the same target and position. |
| `CONCORD011` | Error | The declaration member uses a form Concord does not support. |
| `CONCORD012` | Warning | The patch target type is available at compile time. Write `typeof(Target)` instead of a string. |
| `CONCORD013` | Warning | The target member is available at compile time. Write `nameof(Member)` instead of a string. |
| `CONCORD014` | Warning | The declaration can inherit the target type. Derive from it and use a bare `[Patch]`, which lets C# bind the target's members for you. |
| `CONCORD015` | Error | An injection returns `Control` at a position other than `At.Head`. Only a Head injection can decide whether the original method runs. |
| `CONCORD016` | Error | An around-invoke `Operation` parameter does not match the shape of the matched call. |
| `CONCORD017` | Error | An `At.Constant` or `At.Argument` injection changes the shape of the value it receives. Take and return the matched type. |
| `CONCORD018` | Error | A constant or argument injection uses the wrong `[Inject]` constructor for its position. |
| `CONCORD019` | Error | `At.Argument` with `arg: 0` infers the argument from the parameter type, but more than one call parameter shares that type. Pass `arg:`. |
| `CONCORD020` | Error | The name resolves to a property that has both accessors. Write `get_Name` or `set_Name`. |
| `CONCORD021` | Error | A `[PatchBefore]` or `[PatchAfter]` sits somewhere invalid, names an unknown owner, or conflicts with another rule. |
| `CONCORD022` | Error | A transpiler is not `static`. A `[Patch]` declaration is abstract, so Concord can never call an instance transpiler. |
| `CONCORD023` | Error | A transpiler has the wrong signature. It takes and returns `IEnumerable<CodeInstruction>`, with an optional second `ITranspilerContext` parameter. |
| `CONCORD024` | Error | A transpiler reads a `[Shadow]` or injected member. Concord calls a transpiler instead of copying it, so those members are never rewritten for it. |
| `CONCORD025` | Error | A helper, constructor, or ordinary property reads a `[Shadow]` or injected member. Concord rewrites those reads only inside the bodies it copies, so the helper reads the declaration's own member and gets `null` or a default. Pass the value in as a parameter. |
| `CONCORD026` | Error | One declaration stores two different state types in one slot on one target. Every `SetState` and `GetState<T>` for a target must agree. |
| `CONCORD027` | Warning | A `GetState<T>` has no matching `SetState<T>` anywhere in the declaration, so it reads back `default(T)`. |
| `CONCORD028` | Error | A `[Capture]` parameter sits at a position that matches no call, or at `At.Around` or `At.Argument`, which already hand you the call's arguments. Move it to the `At.Head` or `At.Tail` of an invoke or construction injection. |
| `CONCORD029` | Error | `[Slice]` sits on a position other than an invoke or a construction. A slice bounds a call search, so nothing else can use one. |
| `CONCORD030` | Error | A `[Capture]` ordinal falls outside the matched call's arguments. The ordinal counts from 1. |
| `CONCORD031` | Error | A declaration that extends an enum also declares an `[Inject]` or `[InjectNew]` method. Move the injections to their own `[Patch]` class. |
| `CONCORD032` | Error | An `[EnumMember]` field is not static, or is not typed as the extended enum. Concord assigns members to static fields of that one type. |
| `CONCORD033` | Warning | An extended enum member has an initializer Concord cannot read and overwrites at apply time. Declare the member `const` to pin its value. |
| `CONCORD034` | Error | Two extended enum members in one assembly resolve to the same id. The id is the key Concord stores the value under, so each member needs its own. |
| `CONCORD035` | Warning | A static constructor reads an extended enum member. That constructor can run before `Patcher.Apply` assigns the field, which reads back a zero. |

Concord also ships a diagnostic suppressor. It turns off `CS0649`, `CS0169`, and `CS0414` on an
`[InjectField]` declaration. Those warnings say the field is never assigned or never used, which is
true of the declaration you wrote. Concord assigns and reads the real field at patch time.

## Runtime error codes

When a patch cannot compose, Concord throws a `ConcordEmitException` with a `CONCxxx` code. The code
identifies the condition that Concord rejected.

| Code | Where | What it means |
| --- | --- | --- |
| `CONC002` | Implicit field mapping | A plain declaration field collides by name with a target field, but its type or static form differs. Match the target field, or rename the declaration field when it is only attached-property metadata. |
| `CONC012` | Head | A non-`void` Head injection can cancel the target but never assigns `ReturnValue`. A skipped method still needs a result. |
| `CONC013` | Control or operation handle | The injection stores, captures, or passes a `ControlHandle` or `Operation` family parameter instead of using its supported calls directly. |
| `CONC014` | Invoke-Around splice | The call-site Invoke splice uses a computed expression instead of plain injection-parameter loads. Whole-method Around does not use this splice form; its `Operation.Invoke(...)` accepts computed arguments (see `CONC107` for its own placement rule). |
| `CONC015` | Any position | An injection method returns `Control` somewhere other than a Head injection. Return `void` at other positions. A generic `ControlHandle<T>` may still expose the target's return value at Return or Tail. |
| `CONC031` | Invoke or construction | The call, field read, or construction you named doesn't occur in the target body. Check the declaring type and member name against the actual access. A `[Slice]` narrows the search, and the message then says the declared range instead of the method body. |
| `CONC033` | Invoke or construction | You asked for the `by`-th occurrence of a call site (`by` counts from 1), but fewer than `by` matches exist. A `[Slice]` narrows the count to the declared range, and the message says so. |
| `CONC034` | Return | A `Return` injection found no `return` in the target body to attach to. |
| `CONC035` | Return | You asked for the `by`-th `return` (`by` counts from 1), but the method has fewer returns than that. |
| `CONC036` | Property target | The property has both accessors, but nothing selected its getter or setter. Write `get_Name` or `set_Name`. |
| `CONC037` | Constant | The target body contains no matching literal. |
| `CONC038` | Constant | The requested 1-based literal occurrence does not exist. |
| `CONC039` | Invoke or value injection | The declaration has an unsupported or mismatched call signature, operation handle, argument selector, constant type, or value method signature. |
| `CONC051` | Around | More than one whole-method `Around` injection targets the method. Only one is allowed. Head, Return, and Tail injections can still compose alongside it; see `CONC115` for what cannot. |
| `CONC052` | Patch ordering | `[PatchBefore]` and `[PatchAfter]` rules form a cycle, so Concord cannot choose an order. The message lists the owner loop, such as `A -> B -> A`. Remove or reverse one rule. Concord leaves the installed wrapper unchanged. |
| `CONC060` | Async / iterator | The target is a state machine (`async`/iterator) whose generated `MoveNext` couldn't be found. |
| `CONC061` | Generic target | The target is a generic instantiation with a reference-type argument. The runtime shares one compiled body across all reference-type instantiations, so a detour would leak to every other one. Patch generic targets only at value-type instantiations. |
| `CONC070` | Injected instance | The `[InjectInstance]` declaration is invalid. It must be a single non-static get-only property. |
| `CONC071` | Injected member declaration | An `[InjectField]`, `[InjectProperty]`, or `[InjectMethod]` declaration could not find the named target member or required accessor. |
| `CONC072` | Injected member declaration | An injected member has the wrong type, static form, return type, or signature. This code also covers an `[InjectInstance]` property that cannot receive the target type. |
| `CONC073` | Injected member declaration | An injected member declaration resolves ambiguously. Rename the declaration target or use a more specific signature. |
| `CONC074` | Injected instance | `[InjectInstance]` does not support this target, such as a static method or value-type target. |
| `CONC106` | Tail | A Tail injection found no return in the target body. |
| `CONC107` | Whole-method Around | The `Operation` handle's `Invoke(...)` call sits mid-expression on a target with exception handlers. Splicing the original body clears the evaluation stack on any protected-region exit, so `Invoke(...)` must appear only as a statement, a direct assignment, or a direct return. |
| `CONC108` | Whole-method Around | The target has a `ref`/`out`/`in` (byref) parameter. Byref parameters are not supported by the `Operation` handle. |
| `CONC109` | Whole-method Around | The target has a pointer, function pointer, or byref-like parameter or return type (or returns by reference). These are not supported by the `Operation` handle. |
| `CONC110` | Whole-method Around | The target is an `async` method or an iterator whose body compiles to a state machine. State-machine methods are not supported by the `Operation` handle; patch at Head instead. |
| `CONC111` | Whole-method Around | The injection method must declare exactly one `Operation` family parameter and no `ControlHandle` parameters. Whole-method Around is Operation-only. |
| `CONC112` | Whole-method Around (constructor) | A constructor Around injection never calls `Invoke(...)`. A constructor Around must invoke the original constructor exactly once. |
| `CONC113` | Whole-method Around | The `Operation` handle's `Invoke(...)` call sits inside a loop. The original body can only be spliced once, so Concord rejects a loop that could re-enter the call. |
| `CONC114` | Whole-method Around | The target is a static type initializer (`.cctor`). Type initializers have no coherent Around contract and are not supported. |
| `CONC115` | Whole-method Around | A whole-method `Around` injection is combined with a call-site Invoke, Argument, or Constant injection on the same target. Call-site positions mutate the pre-Around spine, which does not compose with the per-copy splicing a whole-method Around performs. |
| `CONC127` | State slot | One patch declaration puts two types in its state slot on one target. Every `SetState` and `GetState<T>` in the declaration must agree on one type. |
| `CONC128` | Capture | A `[Capture]` parameter sits at a position that matches no call, or at `At.Around` or `At.Argument`, which already receive the call's arguments. Move it to `At.Head` or `At.Tail` of an invoke or construction injection. |
| `CONC129` | Capture | Concord cannot tell where the captured argument finished pushing. A conditional expression in a call argument causes this. Rewrite the argument into a local before the call. |
| `CONC130` | Capture | The capture ordinal runs past the last argument, the parameter type does not match the argument, or the matched site is a field read, which supplies no arguments. |
| `CONC131` | Slice | The target body has no opening anchor at the requested occurrence, or the range names `fromType` without `fromMember` (or the reverse). An anchor is the pair. An anchor also has to be a call or a field read, so a construction cannot serve as one. |
| `CONC132` | Slice | The target body has no closing anchor at the requested occurrence, or the range names `toType` without `toMember` (or the reverse). |
| `CONC133` | Slice | The range is empty or inverted, so it closes at or before it opens. Check the two anchor occurrences against the body order. |
| `CONC134` | Slice | `[Slice]` sits on a position that matches no call site. A range bounds a search, so it applies to invoke and construction positions only. |
| `CONC135` | Extended enum | A member pins a value that is already taken on that enum. |
| `CONC136` | Extended enum | The enum has no free value left in its underlying type. A `[Flags]` enum runs out of bits sooner, because every removed mod keeps the one it used. |
| `CONC137` | Extended enum | A consumer method fits none of the four supported shapes. See [Extended Enums](extended-enums.md#adapter-consumers) for the table. |
| `CONC138` | Extended enum | A declaration that extends an enum also carries an `[Inject]` or `[InjectNew]` method. |
| `CONC139` | Extended enum | A member field is not static, or is not typed as the extended enum. |
| `CONC140` | Extended enum | Two members resolve to the same id. |
| `CONC141` | Extended enum | Concord could not detour one `Enum` method. It logs this and leaves the rest of the set installed. |

Each code identifies the condition Concord rejected. Read the exception message for the target and declaration details.

## Seeing the composed wrapper

### Write it to your desktop

Add `[PatchDebug]` to a patch declaration:

```csharp
[Patch]
[PatchDebug]
abstract class PricePatch : ShopItem
{
    [Inject(At.Tail, nameof(GetPrice))]
    void AfterGetPrice(ControlHandle<int> ch)
    {
        ch.ReturnValue += 5;
    }
}
```

When `Patcher.Apply` applies the declaration, Concord appends the composed wrapper IL to `Concord.PatchDebug.log` on the current user's desktop. Each entry names the target and includes every patch active on that target at that point. The newest entry is at the bottom of the file.

Remove `[PatchDebug]` when you finish troubleshooting.

### Build a dump by hand

Use `WrapperComposer.ComposeDump` when a patch behaves in an unexpected way. It runs the same composition as the patcher. It returns the wrapper IL as text without applying it:

```csharp
using System.Reflection;
using Concord.Emit;

MethodBase target = typeof(ShopItem).GetMethod(nameof(ShopItem.GetPrice))!;
MethodInfo injectionMethod = typeof(PricePatch).GetMethod(
    "AfterGetPrice",
    BindingFlags.NonPublic | BindingFlags.Instance)!;

var injections = new[]
{
    new Injection(injectionMethod, new InjectAt.Tail(), "debug", 0),
};

string il = WrapperComposer.ComposeDump(target, injections);
Console.WriteLine(il);
```

This is a low-level entry point. It uses `Injection` records and `InjectAt` positions instead of `[Patch]` and `[Inject]`. It is mainly useful for work on Concord itself or for hard composition bugs. The dump shows the wrapper spine, copied injection bodies, and lowered control locals. You can see where an injection landed and what the wrapper returns.

If `ComposeDump` throws, it reports the same `CONCxxx` error as a real apply. This lets you inspect the error without installing the patch.
