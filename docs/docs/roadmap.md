# Roadmap

This roadmap covers Concord features that have not shipped. It does not promise a release version or date, and details may change during implementation.

## State shared within one call

Concord plans to add a custom `state` slot to `ControlHandle`. Each target call would get its own value. A `Head` injection could write it, and a `Return` or `Tail` injection could read it later in that call.

## More injection positions

### Read an earlier local

A `Head` Invoke injection uses the target method's normal context. An `Around` Invoke receives the matched call's arguments as leading parameters, and `Operation.Invoke` returns the call result. `At.Argument` receives one selected argument.

Current Invoke forms do not expose other locals that the target computed earlier. Read-side capture would trace a matched call argument back to the local that produced it instead of requiring a slot number. The compiler can change slot numbers after a small source edit, so you would need the low-level IL API to change that local.

### Match a constructor call

Concord plans a matcher for a `newobj` instruction inside a method. Source changes can move this instruction because the matcher depends on compiler output. Head and Tail invoke injections already match field reads.

Concord can patch a constructor body at the head or return of a `.ctor`. See [Patch a constructor](common-tasks.md#patch-a-constructor).

### Limit an Invoke search

Slice and range points would limit Invoke matching to the code between two points. The `by` value would count matches inside that range. You could add the same call outside the range without changing the selected match.

### Edit raw IL

A separate low-level IL API would handle raw return instructions, local writes, and branches. The compiler can renumber locals or rewrite branches after a small source change.

## Enum patching

Concord plans to let multiple mods add members to an existing C# enum. It would assign values in a stable order and expose the new members to `switch`, `Enum.GetValues`, `ToString`, `Enum.Parse`, and flag math.

Use the runtime's own extension system when it has one. Enum patching is meant for a real C# enum in code that a mod cannot change.

The compiler treats enum members as integer constants. It bakes those values into `switch` statements and code that uses flag math. Detours cannot change type metadata before the runtime loads a type, and Concord does not rewrite assemblies.

Concord would need to create each value and patch every consumer that must handle it. Method patches can cover those consumers. Save files need stable values. The same mods in the same order must produce the same integer on each run.
