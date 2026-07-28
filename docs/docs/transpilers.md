# Raw IL with transpilers

A transpiler rewrites a target method's IL instruction by instruction. It is the escape hatch for changes the other `At.*` positions cannot express, such as editing a local, redirecting a branch, or replacing a `ret`.

Transpilers cost more than the other positions. Your code runs against compiler output, so a small edit to the target's source can move the instructions you matched. Reach for a transpiler after you have ruled out the alternatives.

## Try a declarative position first

| You want to | Use this instead |
| --- | --- |
| Run code before or after the method | `At.Head`, `At.Tail` |
| Read or replace a returned value | `At.Return` |
| Wrap the whole method | `At.Around` |
| Change an inlined literal | `At.Constant` |
| Wrap or replace a call | An invoke injection |
| Change one argument of a call | `At.Argument` |

Most of these keep working when the target's body changes, because they match names rather than instructions. `At.Constant` and the `By:` counter on an invoke injection do read compiler output, so recheck those after the target changes. A transpiler reads nothing but compiler output.

## Write a transpiler

A transpiler is a static method that takes the instruction stream and returns a new one:

```csharp
[Patch]
abstract class ShopPatch : Shop
{
    [Inject(At.Transpiler, nameof(GetPrice))]
    static IEnumerable<CodeInstruction> DoublePrice(IEnumerable<CodeInstruction> instructions)
    {
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Is(OpCodes.Ldc_I4, 5))
            {
                yield return new CodeInstruction(OpCodes.Ldc_I4, 10);
            }
            else
            {
                yield return instruction;
            }
        }
    }
}
```

The method must be `static`. A `[Patch]` declaration is abstract and Concord never creates an instance of it, so an instance transpiler cannot run.

Concord calls your transpiler at patch time. It does not copy it into the target the way it copies other injections. So your transpiler cannot touch the declaration's `[Shadow]`, `[InjectField]`, `[InjectProperty]` or `[InjectMethod]` members. Those members are stubs that exist only as IL for Concord to copy. The analyzer rejects this at compile time.

## CodeInstruction

`CodeInstruction` holds one instruction. Its member names match Harmony's, so a transpiler you are porting reads the same:

| Member | Holds |
| --- | --- |
| `opcode` | The `System.Reflection.Emit.OpCode` |
| `operand` | The operand, or `null` |
| `labels` | Branch targets that land on this instruction |
| `blocks` | Exception region markers that open or close here |

`Is(opcode, operand)` tests both at once. Pass `null` as the operand to match any:

```csharp
instruction.Is(OpCodes.Ldc_I4, 5)      // this opcode, this value
instruction.Is(OpCodes.Ldc_I4, null)   // this opcode, any value
instruction.IsLdarg(0)                 // loads argument 0, in any encoding
```

Numbers compare by value, not by box type. Matching `4` finds an operand stored as a `byte`, an `int` or a `long`, which matters because short-form opcodes carry a `byte`. Whole numbers and floating-point numbers never match each other.

## Find code with CodeMatcher

`CodeMatcher` walks the stream with a cursor so you do not have to write the loop:

```csharp
[Inject(At.Transpiler, nameof(GetPrice))]
static IEnumerable<CodeInstruction> DoublePrice(IEnumerable<CodeInstruction> instructions)
{
    return new CodeMatcher(instructions)
        .MatchStartForward(new CodeMatch(OpCodes.Ldc_I4, 5))
        .ThrowIfInvalid("base price literal")
        .SetOperandAndAdvance(10)
        .InstructionEnumeration();
}
```

A failed match sets `Pos` to `-1` and leaves the list alone. Edits on an invalid matcher do nothing, so the chain above will not throw before it reaches `ThrowIfInvalid`. Always call `ThrowIfInvalid` with a message naming what you looked for. Without it a missed match fails silently and your patch does nothing.

`MatchStartForward` searches from the cursor onward and stops on the first instruction of the match. `MatchStartBackwards` searches the other way. Both take several `CodeMatch` values to match a run of instructions.

`CodeMatch` also accepts a predicate when opcode and operand are not enough:

```csharp
new CodeMatch(i => i.operand is MethodInfo m && m.Name.StartsWith("Get"))
```

`InstructionEnumeration()` returns the working list itself, not a copy. Changing that list changes the matcher. It is also the only way to append past the last instruction, since the cursor cannot move there.

## New labels and locals

Add an `ITranspilerContext` parameter when you need to create a label or a local:

```csharp
[Inject(At.Transpiler, nameof(Calculate))]
static IEnumerable<CodeInstruction> SkipWhenZero(
    IEnumerable<CodeInstruction> instructions,
    ITranspilerContext context)
{
    Label skip = context.DefineLabel();
    LocalRef scratch = context.DeclareLocal(typeof(int));
    ...
}
```

`DefineLabel` gives you a branch target. Attach it to an instruction's `labels` to fix where it points. `DeclareLocal` adds a local to the method body. `Original` gives you the method that Concord patches.

A new label that no instruction carries is an error, and so is a branch to a label that nothing declares.

## Rewrite the composed method with At.TranspilerFinal

`At.Transpiler` rewrites the target's original body before Concord adds any other injection. `At.TranspilerFinal` runs at the other end, after Concord splices in every injection.

`At.TranspilerFinal` has no stability guarantee. The composed body is a Concord implementation detail and can change in any release, including a patch release. You will see Concord's own locals, its rewritten returns, and a cancel gate that no source file contains.

Use `At.Transpiler` unless you need to see the finished wrapper.

## Rules your transpiler must follow

**Be a pure function of the stream.** Concord recomposes a target from its original IL every time a mod adds or removes a patch. That includes patches from mods you never heard of. Your transpiler runs an unpredictable number of times. Do not count calls, write to static state, or assume you run once.

**Expect other mods.** Other mods count occurrences with `At.Return(By:)`, `At.Constant(By:)` or an invoke injection's `By:`. If you add or remove a `ret`, a literal or a call, you shift what they count. Concord does not detect this and reports nothing. The other author sees a patch that worked yesterday and no clue why it stopped.

**Set a priority if order matters.** When two mods transpile the same method, the order they run in is not currently stable. Set `Priority` on your `[Inject]` when your edit depends on running before or after someone else's.

## Errors

Transpiler failures raise `ConcordEmitException`. The code is at the front of the message.

| Code | Means |
| --- | --- |
| `CONC116` | The method is not static, or its signature is wrong |
| `CONC117` | Your transpiler threw, or returned `null` |
| `CONC118` | The stream has a bad label, local, opcode or exception block |
| `CONC119` | The rewritten body is not valid IL |
| `CONC120` | A `CodeMatcher` pattern found no match |

Concord drops a failing transpiler and recomposes the target without it. Other mods keep their patches on that method.

## Compile-time checks

The analyzer catches three mistakes before you run:

| Code | Catches |
| --- | --- |
| `CONCORD022` | A transpiler that is not `static` |
| `CONCORD023` | A signature other than `IEnumerable<CodeInstruction>` in and out, with an optional `ITranspilerContext` |
| `CONCORD024` | A transpiler body that reads a shadow field or an injected member |

## Migrating a Harmony transpiler

Most Harmony transpilers do something an `At.*` position already covers. Check [Migrating from Harmony](migration.md) before porting one instruction for instruction.

When you do port one, the shapes line up closely. `CodeInstruction` keeps Harmony's lowercase member names, `CodeMatcher` and `CodeMatch` work the same way, and opcodes keep their encoding across the round trip. Two things differ. Concord passes an `ITranspilerContext` rather than an `ILGenerator`, and labels and locals are opaque handles instead of `Label` and `LocalBuilder` values.
