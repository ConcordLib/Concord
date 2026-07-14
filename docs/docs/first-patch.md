# Your first patch

These examples patch a small `Campfire` type. Each declaration changes one part of its behavior, and the final call applies them together.

## Target type

The example uses a small `Campfire` class that is not tied to a specific runtime:

```csharp
public class Campfire
{
    private int fuel = 10;

    public int GetWarmth()
    {
        return fuel * 2;
    }

    public void BurnFuel(int amount)
    {
        fuel -= amount;
    }
}
```

## Patch declarations

### Change a return value

This patch makes every campfire 5 points warmer:

```csharp
[Patch]
abstract class CampfireWarmthPatch : Campfire
{
    [Inject(At.Tail, nameof(GetWarmth))]
    void AfterGetWarmth(ControlHandle<int> ch)
    {
        ch.ReturnValue += 5;
    }
}
```

`[Patch]` marks a Concord patch declaration. Because the declaration extends `Campfire`, C# can bind its visible members. Concord uses the declaration as metadata and does not create a `CampfireWarmthPatch` object in the target process.

`[Inject(At.Tail, nameof(GetWarmth))]` selects `GetWarmth` and runs before its last `return`. `ControlHandle<int>` exposes the target's `int` return value, and `ch.ReturnValue += 5` adds 5 to it.

> `Tail` runs before the method's last `return`. `Return` runs at each return site, so use it when a method has several exits and each value needs inspection or replacement (including early returns). They behave the same for this single-return `GetWarmth` implementation.

At runtime, calls behave as if the method were:

```csharp
public int GetWarmth()
{
    int result = fuel * 2;
    result += 5;
    return result;
}
```

Concord doesn't touch the `Campfire` source.

### Stop the original method

This patch blocks invalid fuel burns:

```csharp
[Patch]
abstract class CampfireBurnPatch : Campfire
{
    [Inject(At.Head, nameof(BurnFuel))]
    Control BeforeBurnFuel(int amount)
    {
        return amount <= 0 ? Control.Cancel : Control.Continue;
    }
}
```

`At.Head` runs before the target method. Returning `Control.Cancel` skips it, and `Control.Continue` lets it run. An injection holding a `ControlHandle` can call `ch.Cancel()` instead for the same effect.

At runtime:

```csharp
public void BurnFuel(int amount)
{
    if (amount <= 0)
        return;

    fuel -= amount;
}
```

### Access a private field

The target type has a private `fuel` field. Concord lets you declare a matching field in the patch declaration:

```csharp
[Patch]
abstract class CampfireFuelPatch : Campfire
{
    [InjectField("fuel")]
    private int fuel;

    [Inject(At.Tail, nameof(BurnFuel))]
    void AfterBurnFuel(int amount)
    {
        if (fuel < 0)
        {
            fuel = 0;
        }
    }
}
```

`[InjectField("fuel")]` maps the declaration to the target's private field. Concord rewrites each access so the injection reads and writes the real field.

The declared type and static form must match the target field. A missing field fails with `CONC071`; a mismatched declaration fails with `CONC072`.

At runtime:

```csharp
public void BurnFuel(int amount)
{
    fuel -= amount;

    if (fuel < 0)
        fuel = 0;
}
```

## Apply the patches

One call applies every `[Patch]` declaration in your assembly:

```csharp
Patcher.Apply(typeof(CampfireWarmthPatch).Assembly);
```

That applies `CampfireWarmthPatch`, `CampfireBurnPatch`, and `CampfireFuelPatch` together. The call returns an `IPatchHandle`; keep it if the mod needs to remove its patches later. Call `Patcher.Apply` once when the mod starts.

To store new per-instance state instead of accessing a target field, see [Attached Data](attached-data.md).
