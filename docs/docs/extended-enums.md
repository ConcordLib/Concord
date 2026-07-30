# Extended Enums

Several mods can add members to an enum they cannot edit. Concord assigns each new member a value, keeps that value stable across runs, and exposes it to the code that reads the enum.

Read the three limits below before you write a declaration. They decide whether this feature fits your problem.

## What does not work

### `switch` never sees an added member

A C# compiler turns enum members into integer constants. It bakes those constants into every `switch` in the code that was compiled against the enum. Concord installs detours at run time and does not rewrite assemblies, so a compiled `switch` cannot learn about a new member.

Your own code cannot switch on one either. A C# `case` label needs a compile time constant, and Concord assigns the value while the game runs.

Use `if` instead:

```csharp
if (kind == WeatherPatch.Frozen) {
    ApplyFrostDamage(pawn);
}
```

### `ToString` does not report the name

Concord does not detour `Enum.ToString()`. An injection on that method cannot reach the value it runs on. Concord hands a target instance to an injection only through a declaration that extends the target type. No class can extend `System.Enum`, so no declaration can do that here.

String interpolation misses it too. `$"{kind}"` runs through internal runtime code that skips the public method.

For text a player reads, call `Enum.GetName` or register a Display consumer. Both are covered below.

### Values are not the same on every machine

Concord assigns a value the first time it sees a member, then stores it. Two players who install the same mods in a different order still get the same value, because Concord sorts member ids before it assigns anything. Two players who install different mods can get different values for the same member.

Never write an assigned value into a def file, a configuration file, or a network message. Pass the member itself.

## What works with no extra effort

Flag math needs no patching. `|`, `&`, and `HasFlag` run on the underlying integer, so a registered bit behaves like a compiled one:

```csharp
Ability combined = Ability.Swim | AbilityPatch.Glide;

bool canGlide = combined.HasFlag(AbilityPatch.Glide);
```

## Declare members

Add a class that extends `ExtendedEnum<TEnum>` and mark it with `[Patch]`. Each static field typed as the enum becomes a member:

```csharp
using Concord;
using Concord.Orchestration;

[Patch]
public abstract class WeatherPatch : ExtendedEnum<WeatherKind> {
    public static WeatherKind Frozen;
    public static WeatherKind Ashfall;
}
```

Concord assigns both fields during `Patcher.Apply`. Read them like any other enum value after that call.

A static field of any other type is ignored, so the class can still hold a cache or a constant. A declaration that extends an enum cannot also carry `[Inject]` methods. Put injections in their own `[Patch]` class.

### Do not read a member from a static constructor

Concord assigns member fields during `Patcher.Apply`. A static constructor on the declaring class can run first and read a zero. `CONCORD035` reports this.

## Pin a value

Declare the member `const` when it must have one exact value:

```csharp
[Patch]
public abstract class WeatherPatch : ExtendedEnum<WeatherKind> {
    public const WeatherKind Ashfall = (WeatherKind)32;
}
```

`const` is required. A plain static field with an initializer compiles, but Concord cannot read that initializer and overwrites the field during `Patcher.Apply`. `CONCORD033` reports it at compile time.

Concord refuses the patch with `CONC135` when the pinned value is already taken.

## Rename a field and keep its value

Concord stores each member under an id. The default id is the declaring class full name plus the field name, so renaming the field loses the stored value.

Add `[EnumMember]` with the old id to keep it:

```csharp
[Patch]
public abstract class WeatherPatch : ExtendedEnum<WeatherKind> {
    [EnumMember("MyMod.WeatherPatch.Frozen")]
    public static WeatherKind DeepFrost;
}
```

Pick an id once and keep it for the life of the mod. Two members that resolve to the same id are `CONC140` at run time and `CONCORD034` at compile time.

## Expose members to a game

Adding a member is only half the work. The code that reads the enum has to report it. Concord offers two ways to do that, and a runtime adapter sets both up. A mod author usually needs neither.

### Adapter consumers

An adapter registers the game's own enumeration, parse, display, and validity methods:

```csharp
EnumConsumers.For(typeof(WeatherKind))
    .Values(typeof(WeatherUtility), nameof(WeatherUtility.All))
    .Parse(typeof(WeatherUtility), nameof(WeatherUtility.Parse))
    .Display(typeof(WeatherUtility), nameof(WeatherUtility.Label))
    .IsDefined(typeof(WeatherUtility), nameof(WeatherUtility.IsValid));
```

Each method must have one of four shapes. Concord throws `CONC137` when a method fits none of them.

| Consumer | Required shape | What Concord does |
| --- | --- | --- |
| `Values` | Returns `TEnum[]` or `IEnumerable<TEnum>` | Appends the added members to the result |
| `Parse` | `TEnum (string)` | Answers with the added member when the name matches |
| `Display` | `string (TEnum)` | Answers with the member's field name |
| `IsDefined` | `bool (TEnum)` | Answers `true` for an added member |

### Runtime detours

`CoreLibEnumDetours.Install()` routes the `Enum` static methods through the registry:

```csharp
CoreLibEnumDetours.Install();
```

It covers `Enum.GetValues`, `Enum.GetNames`, `Enum.GetName`, `Enum.IsDefined`, `Enum.Parse`, `Enum.TryParse`, and `Enum.Format`. Each detour starts with one dictionary lookup and returns at once for an enum no declaration extends.

Concord measured each detour on an enum no declaration extends, which is what an unaffected call pays:

| Method | No detours | Detours installed |
| --- | --- | --- |
| `Enum.GetName` | 14 nanoseconds, 24 bytes | 23 nanoseconds, 56 bytes |
| `Enum.IsDefined` | 12 nanoseconds, 24 bytes | 19 nanoseconds, 56 bytes |
| `Enum.Format` | 9 nanoseconds, 24 bytes | 15 nanoseconds, 24 bytes |

The wrapper Concord composes accounts for most of that cost, not the registry lookup. Install the set when a game reads enums through these methods. Skip it when your adapter registers a consumer for every enum in play. Pass `Install(includeFormat: false)` to leave `Enum.Format` alone.

A method Concord cannot detour logs `CONC141` and leaves the rest of the set installed. `Enum.TryParse(Type, string, out object)` does not exist on .NET Framework 4.7.2, so that one always logs there.

Results differ by runtime for two methods. On Mono, `Enum.ToString()` and `Enum.Format` both call `Enum.GetName`, so an added member reports its name. On CoreCLR neither does, so an added member reports its number. Do not rely on either.

## How Concord picks a value

1. Concord loads the stored map. An id already in that map keeps its value, always.
2. Concord sorts the remaining ids. This is what makes load order irrelevant.
3. Pinned members go first. A pinned value that is already taken is `CONC135`.
4. The rest follow. A plain enum gets the lowest free integer past its highest existing member. A `[Flags]` enum gets the lowest free bit.
5. Concord saves the map through the adapter's storage.

Running out of room in the underlying type is `CONC136`.

### Removed mods keep their values

Concord never reuses the value of a member it has seen before, even after you remove the mod that declared it. Reinstalling that mod restores the exact integer, so an old save still reads correctly.

This costs room. A `[Flags]` enum has a fixed number of bits, and every removed mod keeps one. Concord warns once when a flags enum drops to three free bits.

## Storage

A runtime adapter supplies the storage through `IEnumValueStore`:

```csharp
public interface IEnumValueStore {
    bool TryLoad(out IReadOnlyDictionary<string, long> map);

    void Save(IReadOnlyDictionary<string, long> map);
}
```

Register it before any mod applies patches:

```csharp
ExtendedEnumRegistry.UseStore(new MyEnumValueStore());
```

Concord logs a warning and keeps values in memory when no store is registered. Those values do not survive a restart.

Save the map with the game state rather than in a global configuration file. Two saves made under different mod sets need different maps. Call `ExtendedEnumRegistry.Reload` when a save supplies a map, and Concord reassigns every member field to the saved values.

## Related pages

[Troubleshooting](troubleshooting.md) lists the `CONC` and `CONCORD` codes on this page.

[Attached Data](attached-data.md) covers the other way to extend a type you cannot edit.

[Packages](packages.md) covers which package an adapter references.
