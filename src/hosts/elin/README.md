# Concord for Elin

[![Discord](https://img.shields.io/badge/Discord-Cryptiks_Mods-5865F2?logo=discord&logoColor=white)](https://discord.gg/tbcKN8e4mZ)
[![Releases](https://img.shields.io/github/v/release/ConcordLib/Elin?label=Release)](https://github.com/ConcordLib/Elin/releases)

Concord is a runtime patching library. This package adds saved per-instance state for Elin mods built on it.

## For players

You need two things:

1. The Concord BepInEx plugin, unzipped into `BepInEx/plugins` (https://github.com/ConcordLib/BepInEx/releases).
2. This package, subscribed on the Workshop or unzipped into `Elin/Package`.

The package tells you in the BepInEx log if the plugin is missing. Mods that depend on Concord will not work without both.

## For mod authors

- Author patches as [Patch] classes that extend the target type, with [Inject] methods at Head, Tail, or Around positions
- Add state to a card (Chara, Thing) by declaring an [Attached] field on the patch. Primitives, strings, enums and plain classes with a parameterless constructor are saved with the game
- Add state to a zone the same way. Zones take int, bool and enum only
- Keep values primitive when a save must outlive your mod. A class value becomes an UnknownTypePlaceholder the first time the game saves without your mod, and that is what comes back when the mod returns
- Duplicated things and charas keep their attached values. Two stackable things with different attached values do not stack
- An [Attached] field is only readable inside its patch class. From anywhere else, `SavedField<TTarget, TValue>.Register("key", typeof(MyPlugin))` gives you a saved field with Get and Set. The owner type namespaces the key by your assembly
- Your plugin cannot BepInDependency this package, so load order is not yours to pick. Register and patch from Awake in any order; Concord.Elin picks up registrations made before it wired

Docs: https://concordlib.dev
Source and issues: https://github.com/ConcordLib/Elin
