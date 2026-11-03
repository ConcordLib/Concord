# Concord.Host.Adapter

**Retired.** `AdapterEntryPoint.Install` doesn't install a backend. Concord patches through its
own native detour, which is what a host without this package gets.

## Use

Call the entry point from your mod's constructor:

```csharp
Selection choice = AdapterEntryPoint.Install(log: null, setting: null, assemblyDirectories: null);
```

`choice.Waylith` is always `false`, and `choice.Reason` holds `AdapterEntryPoint.RetiredReason`.
Write `choice.Message` to your log. The `setting` argument and the `CONCORD_WAYLITH_BACKEND`
environment variable are both ignored.

Pass `assemblyDirectories` anyway. The call uses it to register the file lookup that
`[Local(Name)]` needs on a mod assembly the host loads from bytes.

## Driving the backend yourself

The body-replacement backend is still in this assembly, and one overload still installs it:

```csharp
Selection choice = AdapterEntryPoint.Install(log, setting, assemblyDirectories, support, backend);
```

It takes a capability verdict and a factory that wraps the current backend, so no probe decides
anything. The tests use it. If you call it, you handle whatever the runtime does with the body.

Body replacement needs a runtime that can replace a managed method body. RimWorld 1.7 includes a
CoreCLR fork from Ludeon that can. `RuntimeCapability.Explain()` says what the running one offers.
The assembly has no compile-time reference to that fork. It resolves the type by name and calls it
through reflection.

## Tests

`Concord.Host.Adapter.Tests` runs anywhere and is part of the solution.

`Concord.Host.Adapter.ForkTests` needs a RimWorld 1.7 install and is deliberately outside both
solution files. Run it by path:

```bash
dotnet build tests/Concord.Host.Adapter.ForkTests -p:ForkRuntimeDir=<install>
./tests/Concord.Host.Adapter.ForkTests/bin/Debug/net10.0/linux-x64/Concord.Host.Adapter.ForkTests
```

Without that install the build fails on purpose. Off-runtime the facts would report as skipped and
nothing would say so.
