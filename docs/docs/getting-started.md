# Getting started

Concord is a runtime patching library for .NET mods. A mod describes a change in C#, and Concord applies it after the mod loads.

## Prerequisites

Before you start, you need:

- A C# mod project that already builds and loads in the target runtime.
- A reference to each target assembly that contains code you want to patch.
- A target runtime or runtime adapter that loads the Concord Assembly (`Concord.dll`), like the RimWorld Concord mod.

These guides assume you can read C# classes, methods, attributes, inheritance, and generic types. You don't need to know how IL or runtime detours work.

## Reference Concord

Add `Concord.Ref` to your project file:

```xml
<ItemGroup>
  <PackageReference Include="Concord.Ref" Version="0.7.0" />
</ItemGroup>
```

`Concord.Ref` contains the public API your mod needs at compile time, while the target runtime supplies the working implementation. Your project must also reference each target assembly that defines a type you want to patch. Those references are specific to the game or host, so follow its mod project setup for the correct paths.

### Optional build tools

`Concord.Analyzers` and `Concord.Generators` are optional, and patches compile and run without either package.

| Package | What it adds |
| --- | --- |
| `Concord.Analyzers` | Editor and build checks for target methods, parameters, and injected members |
| `Concord.Generators` | A generated patch registry, typed shadow members, and IDE refactorings |

Add the package reference for each tool you want. Keep `PrivateAssets="all"` so the tool stays in your build and does not become a dependency of your mod:

```xml
<ItemGroup>
  <PackageReference Include="Concord.Analyzers" Version="0.7.0" PrivateAssets="all" />
  <PackageReference Include="Concord.Generators" Version="0.7.0" PrivateAssets="all" />
</ItemGroup>
```

If you only want one tool, include only its line; the [Packages](packages.md) page has more detail about both packages.

## What patching is

Runtime patching changes what an existing method does after the game or app starts. It leaves the source file and target DLL unchanged, and other code still calls the same method.

A patch can run at the start or end of a method, change arguments or return values, skip the original body, or wrap a call made inside it. For example, if `GetPrice()` normally returns `10`, a patch can make it return `15` while the mod is loaded.

## How Concord works

A patch names a target type, a target method, and the point where its injection runs. `Patcher.Apply` finds those declarations. Concord copies the target method body into a new wrapper, adds the injections, and installs a runtime detour that sends calls through the wrapper.

Disposing the returned patch handle removes its injections. Concord then rebuilds the wrapper for any patches that remain, or removes the detour when none remain. [How patches work](how-patches-work.md) covers the full process.

### Current limits

- Concord changes method behavior, not the structure of a type. It cannot add real fields, interfaces, enum members, or other type metadata.
- The public authoring API does not support async methods, iterators, or static constructors yet.
- Concord rejects generic targets with reference-type arguments because the runtime shares their compiled method bodies. Value-type generic targets can be patched.
- Concord cannot yet target a local variable, branch, field write, or object construction inside a method. Head and Tail invoke injections can target field reads. Concord can also patch a constructor's body.

See the [Roadmap](roadmap.md) for planned injection targets and other work that has not shipped yet.

## Simple example

### Choose a target method

A patch starts with an existing type and method, such as this `ShopItem.GetPrice()` method that returns a price:

```csharp
public class ShopItem
{
    public int GetPrice()
    {
        return 10;
    }
}
```

The mod project must reference the assembly that defines `ShopItem` so the C# compiler can resolve the target type, its visible members, and the method name.

### Write the patch

Create an abstract class that extends the target type and mark it with `[Patch]`:

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

`[Patch]` marks `PricePatch` as a patch declaration, and extending `ShopItem` selects the target type. Concord reads the class as a declaration instead of creating a `PricePatch` object.

`[Inject(At.Tail, nameof(GetPrice))]` selects `GetPrice()` and runs the injection before its last `return`. The `ControlHandle<int>` parameter gives the injection access to the returned `int`, so the last line adds 5 to the price.

After the patch is applied, `GetPrice()` returns `15`, but Concord doesn't change the source file or the assembly on disk. It creates the new behavior in memory while the target process runs.

### Apply the patch

Call `Patcher.Apply` when your mod starts:

```csharp
public sealed class MyMod
{
    private readonly IPatchHandle patches;

    public MyMod()
    {
        patches = Patcher.Apply(typeof(MyMod).Assembly);
    }
}
```

The call finds every `[Patch]` declaration in the assembly and applies them together. With `Concord.Generators`, it reads the generated registry; without the generator, it finds the declarations through reflection. Both paths apply the same patches.

Keep the returned `IPatchHandle` if your mod needs to remove its patches later, then call `Dispose()` on the handle to remove them. Applying the same assembly again returns the same handle instead of adding the patches twice.

An ordinary patch does not need MonoMod calls, hand-written IL, or Concord's internal emit and detour APIs.

[Your First Patch](first-patch.md) shows how to cancel a method and access a private field. If you are moving from Harmony, see [Migrating from Harmony and Prepatcher](migration.md).
