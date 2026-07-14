# Start Here

Concord changes method behavior in a running target process. A mod supplies a C# patch, and Concord runs it at the selected point in the target method. Concord does not modify the target assembly on disk.

Say the target has a method like this:

```csharp
public class ShopItem
{
    public int GetPrice()
    {
        return 10;
    }
}
```

A Concord patch can run before or after `GetPrice()`. It can also change the return value, skip the method, or access private fields on the target object.

You write patch declarations in C#. Concord handles the generated wrapper and runtime detour.

## Start with the authoring packages

When you author a mod, reference `Concord.Ref`, not the Concord Assembly (`Concord.dll`). The ref package gives the compiler and Rider/Visual Studio the Concord API surface, while the target runtime supplies the Concord Assembly.

Add `Concord.Analyzers` for build checks and `Concord.Generators` for generated patch registries, shadow members, and IDE refactorings. Both are build-time tools, so keep them private to your project:

```xml
<ItemGroup>
  <PackageReference Include="Concord.Ref" Version="0.7.0" />
  <PackageReference Include="Concord.Analyzers" Version="0.7.0" PrivateAssets="all" />
  <PackageReference Include="Concord.Generators" Version="0.7.0" PrivateAssets="all" />
</ItemGroup>
```

These packages catch authoring errors during the build and generate the code Concord needs without adding a runtime implementation to your mod. The target runtime provides that implementation through the Concord Assembly.

## Prerequisites

These guides assume you can read C# methods, classes, attributes, generics, and inheritance. They do not assume prior runtime-patching experience.

## The vocabulary

A **patch** is a runtime change Concord applies to a **target method**, the existing method whose behavior you want to change. The target method lives in the target runtime, not in your mod. An **injection** is a `[Inject]` declaration that tells Concord where your injection method runs.

Two types control what happens to the original method. A **control handle** is an optional `ControlHandle` parameter. Its generic form exposes the target's return value, and a Head injection can also use it to cancel the target. **Control** is what a Head injection can return to choose whether the original runs: `Control.Continue` or `Control.Cancel`.

A **shadow field** is a field on the patch declaration marked with `[InjectField]`. It stands in for a private field on the target type.

## A complete patch

A return-value patch looks like this:

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

`[Patch]` marks the declaration. Extending `ShopItem` selects `ShopItem` as the target type. `[Inject]` names the position and target method; `At.Tail` runs before the last `return` of `GetPrice`.

`ControlHandle<int>` exposes the method's `int` return value. This injection adds 5 to whatever `GetPrice` returned.

Use `At.Return` when a method has several exits and each returned value needs inspection or replacement. For a single-exit method like this one, `Tail` and `Return` do the same thing.

Calls then behave as if the method had been written like this:

```csharp
public int GetPrice()
{
    int result = 10;
    result += 5;
    return result;
}
```

But Concord doesn't rewrite the source. It creates this behavior at runtime.

Next: [Getting Started](getting-started.md), then [Your First Patch](first-patch.md).
