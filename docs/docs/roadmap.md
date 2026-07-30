# Roadmap

This roadmap covers Concord features that have not shipped. It does not promise a release version or date, and details may change during implementation.

## A stable order for transpilers

Two mods can transpile the same method. The order Concord runs them in is not stable today, so the
result depends on load order. Set `Priority` on your `[Inject]` when your edit depends on running
before or after another mod's transpiler. See [Rules your transpiler must follow](transpilers.md#rules-your-transpiler-must-follow).

Concord plans to give transpilers that set no priority a defined order. A patch that works today and
fails tomorrow, with no source change on either side, is the worst kind of bug to chase.
