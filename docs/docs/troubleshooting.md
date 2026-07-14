# Troubleshooting

When a patch cannot compose, Concord throws a `ConcordEmitException` with a `CONCxxx` code. The code identifies the condition that Concord rejected. This page lists the runtime composition codes and shows how to inspect the wrapper.

## Error codes

| Code | Where | What it means |
| --- | --- | --- |
| `CONC002` | Implicit field mapping | A plain declaration field collides by name with a target field, but its type or static form differs. Match the target field, or rename the declaration field when it is only attached-property metadata. |
| `CONC012` | Head | A non-`void` Head injection can cancel the target but never assigns `ReturnValue`. A skipped method still needs a result. |
| `CONC013` | Control or operation handle | The injection stores, captures, or passes a `ControlHandle` or `Operation` family parameter instead of using its supported calls directly. |
| `CONC014` | Invoke-Around splice | The call-site Invoke splice uses a computed expression instead of plain injection-parameter loads. Whole-method Around does not use this splice form; its `Operation.Invoke(...)` accepts computed arguments (see `CONC107` for its own placement rule). |
| `CONC015` | Any position | An injection method returns `Control` somewhere other than a Head injection. Return `void` at other positions. A generic `ControlHandle<T>` may still expose the target's return value at Return or Tail. |
| `CONC031` | Invoke | The call site you named doesn't occur in the target body. Check the declaring type and method name against the actual call. |
| `CONC033` | Invoke | You asked for the `by`-th occurrence of a call site (`by` counts from 1), but fewer than `by` matches exist in the body. |
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
| `CONC074` | Injected instance | `[InjectInstance]` cannot be used on this target, such as a static method or value-type target. |
| `CONC106` | Tail | A Tail injection found no return in the target body. |
| `CONC107` | Whole-method Around | The `Operation` handle's `Invoke(...)` call is used mid-expression on a target with exception handlers. Splicing the original body clears the evaluation stack on any protected-region exit, so `Invoke(...)` must appear only as a statement, a direct assignment, or a direct return. |
| `CONC108` | Whole-method Around | The target has a `ref`/`out`/`in` (byref) parameter. Byref parameters are not supported by the `Operation` handle. |
| `CONC109` | Whole-method Around | The target has a pointer, function pointer, or byref-like parameter or return type (or returns by reference). These are not supported by the `Operation` handle. |
| `CONC110` | Whole-method Around | The target is an `async` method or an iterator whose body compiles to a state machine. State-machine methods are not supported by the `Operation` handle; patch at Head instead. |
| `CONC111` | Whole-method Around | The injection method must declare exactly one `Operation` family parameter and no `ControlHandle` parameters. Whole-method Around is Operation-only. |
| `CONC112` | Whole-method Around (constructor) | A constructor Around injection never calls `Invoke(...)`. A constructor Around must invoke the original constructor exactly once. |
| `CONC113` | Whole-method Around | The `Operation` handle's `Invoke(...)` call sits inside a loop. The original body can only be spliced once, so a loop that could re-enter the call is rejected. |
| `CONC114` | Whole-method Around | The target is a static type initializer (`.cctor`). Type initializers have no coherent Around contract and are not supported. |
| `CONC115` | Whole-method Around | A whole-method `Around` injection is combined with a call-site Invoke, Argument, or Constant injection on the same target. Call-site positions mutate the pre-Around spine, which does not compose with the per-copy splicing a whole-method Around performs. |

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
