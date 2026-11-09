Concord is a runtime patching library for RimWorld mods. Mod authors write [Patch]/[Inject] templates (similar to Java's Mixin library) instead of Harmony-style prefix and postfix methods, and get saved per-instance state and custom XML def fields on top.

## For players

Load Concord before every mod that depends on it. Put it at the top of your mod list, above the core game. There is nothing to configure.

Mods that depend on Concord will not work without it.

## For mod authors

- Author patches as [Patch] classes that extend the target type, with [Inject] methods at Head, Tail, or Around positions
- Add state to a thing, pawn, hediff or comp by declaring an [Attached] field on the patch. It reads like a normal field and it is written to the save
- Add custom XML fields to defs, and Concord lifts them before vanilla parsing
- The Concord runtime ships as 0Concord.dll and loads before your mod

### Examples

A patch extends the type it patches, so target members are in scope. Change a return value by injecting at the tail:

```csharp
[Patch]
abstract class ThingInspectPatch : Thing {
    [Inject(At.Tail, nameof(GetInspectString))]
    void AfterGetInspectString(ControlHandle<string> ch) {
        ch.ReturnValue += "\nConcord was here.";
    }
}
```

Cancel a method before it runs. No more zzzt:

```csharp
[Patch]
abstract class ShortCircuitPatch : IncidentWorker_ShortCircuit {
    [Inject(At.Head, nameof(TryExecuteWorker))]
    void BeforeTryExecuteWorker(ControlHandle<bool> ch) {
        ch.ReturnValue = false;
        ch.Cancel();
    }
}
```

TryExecuteWorker is protected. That works because the patch extends the target type, which puts protected members and nameof in reach.

Give every thing its own damage tally, saved with the game:

```csharp
[Patch]
abstract class DamageTallyPatch : Thing {
    [Attached]
    float totalDamage;

    [Inject(At.Tail, nameof(TakeDamage))]
    void AfterTakeDamage(DamageInfo dinfo) {
        totalDamage += dinfo.Amount;
    }
}
```

`totalDamage` is not a field on `Thing`. Concord stores it beside each instance and writes it into the save, so there is nothing to scribe yourself.

Apply your patches once, from your mod's constructor:

```csharp
public class MyMod : Mod {
    public MyMod(ModContentPack content) : base(content) {
        Concord.Patcher.Apply(typeof(MyMod).Assembly);
    }
}
```

Docs: https://concordlib.dev
Source and issues: https://github.com/ConcordLib/RimWorld
