# Roadmap

This roadmap covers Concord features that have not shipped. It does not promise a release version or date, and details may change during implementation.

## Enum patching

Concord plans to let multiple mods add members to an existing C# enum. It would assign values in a stable order and expose the new members to `switch`, `Enum.GetValues`, `ToString`, `Enum.Parse`, and flag math.

Use the runtime's own extension system when it has one. Enum patching is meant for a real C# enum in code that a mod cannot change.

The compiler treats enum members as integer constants. It bakes those values into `switch` statements and code that uses flag math. Detours cannot change type metadata before the runtime loads a type, and Concord does not rewrite assemblies.

Concord would need to create each value and patch every consumer that must handle it. Method patches can cover those consumers. Save files need stable values. The same mods in the same order must produce the same integer on each run.
