# The composed body contract

A composed body is Concord's description of a wrapper it just built, written with no Cecil type in it. It exists for one reader. That reader is a detour backend in another assembly. Such a backend encodes the wrapper itself, instead of taking the method Concord already emitted.

If you write patches, you never need this page. If you write a backend that consumes `ComposeResult.Body`, this page is the contract you are coding against.

## Turn capture on

Capture is off by default, because the normal detour path reads none of it and describing a body costs a conversion.

```csharp
using Concord.Emit;

WrapperComposer.CaptureComposedBody = true;
```

Set the gate before you apply any patch you want to read. The setter also turns on per-instruction origin recording. Origins are recorded during the compose, so a body composed while the gate was off has no provenance to report later.

After a compose, read the result:

```csharp
ComposeResult result = /* the compose you drove */;

if (result.Body is ComposedBody body)
{
    Encode(body);
}
```

## What the body contains

| Member | Type | What it holds |
| --- | --- | --- |
| `Instructions` | `IReadOnlyList<ComposedInstruction>` | The instruction stream in emission order. |
| `Locals` | `IReadOnlyList<ComposedLocal>` | The local slots in slot order, so slot `n` is `Locals[n]`. |
| `Clauses` | `IReadOnlyList<ComposedClause>` | The exception-handling regions, as indices into `Instructions`. |
| `InitLocals` | `bool` | Whether the header's `init` flag is set. |

### Instructions

Each `ComposedInstruction` carries a `CodeInstruction` plus its provenance. The operand is already resolved to a CLR type. It is a `Label`, a `Label[]`, a `LocalRef`, a `Type`, a `FieldInfo`, a `MethodBase`, or an inline constant.

Branch targets are `Label` values, never byte offsets. A branch names the label that appears in the `labels` list of the instruction it jumps to.

Local references are `LocalRef` values that name a slot in `Locals`.

### Locals

`ComposedLocal` has two members: `Type` and `IsPinned`. The pinned flag is separate because `System.Type` cannot express the `PINNED` modifier. Concord strips the pin, and any `modopt` or `modreq`, off the type it hands you.

### Clauses

`ComposedClause` describes one exception-handling region. `Kind` is `Catch`, `Filter`, `Finally`, or `Fault`, and it tells you which of the kind-specific members carry a value.

| Member | Meaning |
| --- | --- |
| `TryStart` | First instruction of the protected region, inclusive. |
| `TryEnd` | One past the last instruction of the protected region. |
| `HandlerStart` | First instruction of the handler, inclusive. For a filter, where the filter hands over. |
| `HandlerEnd` | One past the last instruction of the handler. |
| `CatchType` | The caught type when `Kind` is `Catch`, otherwise `null`. |
| `FilterStart` | First instruction of the filter when `Kind` is `Filter`, otherwise `-1`. The filter runs up to `HandlerStart`. |

Four rules decide whether your encoder produces the right regions. Getting one wrong is a silent wrong-code bug, not a crash, so read them twice:

1. Every boundary is an index into `Instructions`. None of them is a byte offset.
2. Every end is exclusive. `TryEnd` and `HandlerEnd` both name the instruction after the region.
3. `HandlerEnd == Instructions.Count` means the handler runs to the literal end of the body. Resolve it the way you resolve any other boundary, by marking a label after the last instruction. Treating it as `Count - 1` truncates the handler by one instruction.
4. `FilterStart` is `-1` on any clause that is not a filter. It is not an index in that case.

Rule 3 is verified in Concord's own test suite against real compiled bodies:

| Test | What it asserts |
| --- | --- |
| `ComposedBodyFactoryTests.cs:36` | On a compiled body whose handler runs to the end, `HandlerEnd` equals the instruction count and does not equal count minus one. |
| `ComposedBodyFactoryTests.cs:53` | The same result on a hand-built `finally` with a null end. |
| `ComposedBodyFactoryTests.cs:67` | The neighbouring case, a try region that ends at the last instruction, keeps its exclusive end. |
| `CecilCodeConverterRoundTripTests.cs:118` | A handler that runs to the end survives a full round trip and comes back with its end still open. |

## Provenance: origin, not ownership

Each instruction came from one of three places, and Concord tells you which:

| `OriginOffset` | `OriginInjection` | Where the instruction came from |
| --- | --- | --- |
| Set | `null` | The target spine. The value is the IL offset in the original target body. |
| `null` | Set | An injection. The value is the injection method, and its declaring type's assembly names the mod. |
| `null` | `null` | Neither. A transpiler created it, which is legitimate. |

The two are never both set. If a copier ever records both, Concord reports `CONC118` in `BodyRejection` and leaves `Body` null, because at that point neither answer is trustworthy. The compose still finishes and still returns a working wrapper. `OriginOffset` is an `int?` rather than a sentinel, so an instruction that really came from offset 0 cannot read as having no origin.

**These fields describe origin, not ownership.** Spine IL that Concord splices inside an `Around` injection's control flow still reports the target as its origin. That is the right answer for a line number, because the instruction really is the target's code. It also means a large spine count on an `Around` body does not mean the `Around` contributed little. Do not read these counts as a measure of who wrote the method.

Here is what real game and mod methods look like, from Concord's compose dump harness. The columns are spine, injection, and neither:

| Instructions | Spine | Injection | Neither | Handlers |
| --- | --- | --- | --- | --- |
| 32 | 28 | 0 | 4 | 0 |
| 777 | 736 | 38 | 3 | 3 |
| 39 | 3 | 32 | 4 | 1 |
| 277 | 32 | 243 | 2 | 3 |
| 111 | 19 | 88 | 4 | 2 |

Every row sums to the instruction count, because every instruction lands in exactly one of the three buckets. Row two is the case to look at. An `Around` over a target of roughly 700 instructions reports 736 spine instructions. The `Around` still drives the method.

## What the consumer is responsible for

The contract is deliberately narrow. Four jobs belong to you.

### Resolve operands to metadata tokens

Concord hands you `Type`, `FieldInfo`, and `MethodBase` objects. Your backend resolves each one to a metadata token against its own target module. Concord cannot do this, because it does not know which module you are writing into.

### Encode the locals signature structurally

Walk `Locals` and encode each slot from its `Type`, recursively, and set the pinned flag from `IsPinned`.

A token callback cannot do this job. A locals signature rejects a `TypeSpec` where it wants a `TypeDefOrRef`, so a single token for `List<int>` is not accepted. Arrays and byref types have the same problem. All three need structural encoding, element type by element type.

### Derive `maxstack` and any dynamic-stack-allocation flag

The contract carries neither value, by design.

A runtime that trusts the header value costs nothing for a generous overestimate, so you can compute a safe bound from the instruction stream. The alternative is worse: it would put a stack-depth walker inside Concord, maintained for every target framework Concord supports, to serve one consumer. Derive both from the stream instead.

### Build the old-to-new map

The contract carries indices, not byte offsets. Your encoder is what turns an index into a byte offset, so only you know the new offsets.

`OriginOffset` is the other half of the map. Pair each instruction's `OriginOffset` with the byte offset your encoder gives it. That pairing gives you an old-to-new offset map for stack traces and debug information. Concord supplies the origin half and nothing more.

## What Concord refuses

Some composed bodies carry something that no metadata-token API can express. Concord checks all three token-bearing channels, which are instruction operands, local signatures, and catch types. When it finds one, it reports `CONC157` and does not hand you a body.

Refusals cover:

- `calli` call sites and `vararg` calls, whose operand is a standalone signature rather than a member.
- `jmp`, which no encoder accepts.
- Anything reachable from a dynamic assembly. This includes a generic instantiation over a dynamic type, where only the generic arguments name the dynamic assembly.

A refusal is not a failure. The patch still applies through the existing detour backend, which handles all of these. Refusing a body that a detour already handles would turn a working patch into a crash.

## Verified and not verified

This page is what a backend author trusts, so it says which parts are proven.

**Verified.** The handler-runs-to-end rule, in the four tests listed earlier on this page. The stream-level refusal in `CONC157` is also well covered by tests.

**Not verified end to end.** `ComposeResult.BodyRejection` set with `Body` null is designed and written, but never exercised through a whole compose. Every reference to it in the repository today is an `Assert.Null` or a failure-message interpolation. The refusal itself is tested. The path that turns a refusal into a null body with a populated reason has not run. A later release closes that gap. Until it does, do not treat a populated `BodyRejection` as behaviour anyone has observed.

## Related

- [Raw IL with transpilers](transpilers.md) explains `CodeInstruction`, `Label`, and `LocalRef`.
- [Troubleshooting](troubleshooting.md) lists `CONC157` and every other runtime code.
