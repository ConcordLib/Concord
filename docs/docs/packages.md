# Packages

Concord publishes five NuGet packages. The package you need depends on what you are building.

Use the same version for every Concord package in a project. The examples on this page use `0.14.0`.

## Choose what to reference

| Project | Required | Optional |
| --- | --- | --- |
| A mod for a runtime that already loads Concord | `Concord.Ref` | `Concord.Analyzers` and `Concord.Generators` |
| A runtime adapter or host | `Concord.Runtime` | `Concord.Harmony`, when the game ships Harmony |
| Concord itself | The projects in the Core repository | Analyzer and generator projects as needed |

Mods compile against `Concord.Ref`. The target runtime loads the implementation from `Concord.dll`, so a mod should not reference or ship `Concord.Runtime`.

## Mod projects

### Concord.Ref

`Concord.Ref` is a metadata-only reference assembly. It gives the compiler and IDE access to Concord's public API, including `[Patch]`, `[Inject]`, `ControlHandle<T>`, `Patcher`, and `AttachedField<,>`.

Add it to the mod project:

```xml
<ItemGroup>
  <PackageReference Include="Concord.Ref" Version="0.14.0" />
</ItemGroup>
```

The package contains no runtime implementation. Its assembly identity matches the Concord Assembly, so code compiled against `Concord.Ref` binds to the `Concord.dll` loaded by the target runtime.

`Concord.Ref` provides `net10.0` and `netstandard2.0` assets. A .NET Framework 4.7.2 mod can compile against the `netstandard2.0` asset.

### Optional build tools

Patches compile and run without the analyzer or generator packages. Add either tool when you want the checks or generated code it provides. Keep `PrivateAssets="all"` so it stays in the build and does not become a dependency of the mod.

```xml
<ItemGroup>
  <PackageReference Include="Concord.Analyzers" Version="0.14.0" PrivateAssets="all" />
  <PackageReference Include="Concord.Generators" Version="0.14.0" PrivateAssets="all" />
</ItemGroup>
```

#### Concord.Analyzers

`Concord.Analyzers` reports patch mistakes in the compiler and IDE. It checks control and operation handles, target names, injection signatures, and injected members. It also checks transpiler shape, state slots, captures, slices, and patch ordering. Each check runs when the analyzer can resolve the target from the project. [Troubleshooting](troubleshooting.md#analyzer-diagnostics) lists every diagnostic it reports.

It also suppresses field-use warnings for valid `[InjectField]` declarations and suggests compiler-checked forms such as `typeof` and `nameof` when they are available.

#### Concord.Generators

Roslyn runs source generators while it compiles your project. Once you reference `Concord.Generators`, build the mod as usual; the compiler adds the generated C# to the mod assembly.

The patch registry generator writes an assembly-level list of every `[Patch]` class. `Patcher.Apply` reads that registry instead of searching every type through reflection, but falls back to reflection when no registry exists.

The shadow-member generator handles `[Shadow]` on partial patch classes. `[Shadow("hitPoints")]` adds a typed member to the generated part of the class, so patch code can use `this.hitPoints` as a normal C# field. When Concord builds the patch, it rewrites that access to the real `hitPoints` field on the current target object. It can also generate declarations for private properties and methods; see [Generate private member declarations](common-tasks.md#generate-private-member-declarations) for an example.

The package also has IDE actions that can create patches, injections, and shadow members. Those editor actions are separate from the source generators.

### Use a local Core checkout

You can reference a sibling Core checkout while developing against changes that have not been published. The explicit assembly reference keeps the reference assembly out of the mod output. The project references only set the build order.

```xml
<PropertyGroup>
  <ConcordRoot Condition="'$(ConcordRoot)' == ''">$(MSBuildProjectDirectory)/../../Concord/Core</ConcordRoot>
  <ConcordRefTargetFramework Condition="'$(TargetFramework)' == 'net10.0'">net10.0</ConcordRefTargetFramework>
  <ConcordRefTargetFramework Condition="'$(ConcordRefTargetFramework)' == ''">netstandard2.0</ConcordRefTargetFramework>
  <ConcordRefDll Condition="'$(ConcordRefDll)' == ''">$(ConcordRoot)/src/Concord.Ref/bin/$(Configuration)/$(ConcordRefTargetFramework)/Concord.dll</ConcordRefDll>
  <ConcordAnalyzerDll Condition="'$(ConcordAnalyzerDll)' == ''">$(ConcordRoot)/Assemblies/Concord.Analyzers.dll</ConcordAnalyzerDll>
  <ConcordGeneratorDll Condition="'$(ConcordGeneratorDll)' == ''">$(ConcordRoot)/Assemblies/Concord.Generators.dll</ConcordGeneratorDll>
</PropertyGroup>

<ItemGroup>
  <Reference Include="Concord">
    <HintPath>$(ConcordRefDll)</HintPath>
    <Private>false</Private>
  </Reference>

  <Analyzer Include="$(ConcordAnalyzerDll)" />
  <Analyzer Include="$(ConcordGeneratorDll)" />

  <ProjectReference Include="$(ConcordRoot)/src/Concord.Ref/Concord.Ref.csproj"
                    ReferenceOutputAssembly="false"
                    PrivateAssets="all" />
  <ProjectReference Include="$(ConcordRoot)/src/Concord.Analyzers/Concord.Analyzers.csproj"
                    ReferenceOutputAssembly="false"
                    PrivateAssets="all" />
  <ProjectReference Include="$(ConcordRoot)/src/Concord.Generators/Concord.Generators.csproj"
                    ReferenceOutputAssembly="false"
                    PrivateAssets="all" />
</ItemGroup>
```

Remove the analyzer or generator lines if you do not use that tool. Build the selected Core projects once before opening or reloading the mod project in Rider:

```bash
dotnet build ../../Concord/Core/src/Concord.Ref/Concord.Ref.csproj
dotnet build ../../Concord/Core/src/Concord.Analyzers/Concord.Analyzers.csproj
dotnet build ../../Concord/Core/src/Concord.Generators/Concord.Generators.csproj
```

If Rider still marks `using Concord;` or `[Patch]` as unresolved, reload the project so Rider reads the new output paths.

## Runtime adapters

### Concord.Runtime

`Concord.Runtime` is for a host or adapter that loads Concord into a target process. The package contains the merged Concord Assembly, `Concord.dll`:

```xml
<ItemGroup>
  <PackageReference Include="Concord.Runtime" Version="0.14.0" />
</ItemGroup>
```

The package provides `net10.0`, `netstandard2.0`, and `net472` assemblies. It has no NuGet dependencies because the build folds the four runtime libraries and their MonoMod dependencies into each `Concord.dll`.

The package ID is `Concord.Runtime` because the plain `Concord` ID on NuGet belongs to an unrelated package.

### Concord.Harmony

`Concord.Harmony` lets Concord patches share a method with Harmony patches. Add it to an adapter for
a game that ships Harmony. A mod does not reference it.

```xml
<ItemGroup>
  <PackageReference Include="Concord.Harmony" Version="0.14.0" />
</ItemGroup>
```

The package holds one small assembly for `net10.0`, `netstandard2.0`, and `net472`. Concord does not
merge it into `Concord.dll`, because it needs a `0Harmony` reference and the Concord Assembly keeps
third-party types out of its public metadata.

The package declares no NuGet dependencies. The host supplies both at run time: the target runtime
loads `Concord.dll`, and the game loads `0Harmony`. Your adapter loads this assembly only after it
finds a supported Harmony version. Concord's tests cover the Harmony 2.4 line.

[Harmony compatibility](harmony-compatibility.md) describes what the bridge does once it loads.

## Platform support

Concord runs on .NET 10 through CoreCLR and on .NET Framework 4.7.2. The `net472` assembly also runs on Mono, which is what Unity games and older game runtimes use.

### Concord cannot patch a function-pointer method on Mono

On a Mono host, Concord cannot patch a method whose body declares a `delegate*` local. The attempt kills the process. The same applies to an injection method that declares one, so a mod can trip this on its own code.

Mono 6.12 reads `Type.IsValueType` on a function-pointer type and recurses until the native stack overflows. Concord opens each target and each injection method through MonoMod's `DynamicMethodDefinition`, whose constructor reads that property for every local. So the crash reaches any method Concord opens that declares such a local.

Treat this as a hard limitation rather than an error you can handle:

* The overflow happens in unmanaged code. It is not a `ConcordEmitException`, and a `try`/`catch` cannot contain it.
* Concord reports no diagnostic first. The process dies while Concord composes the wrapper, which happens on apply and on any later recompose.
* A mod cannot test for the condition ahead of time through Concord.

Function-pointer locals are rare in game and mod code, so most patches never reach this. When a target does declare one, leave that method unpatched on a Mono host, and keep `delegate*` locals out of your own injection methods there. CoreCLR reads the same local without trouble, so a .NET 10 host patches that target normally.

## Core contributors

The Core repository builds the runtime from four internal projects:

| Project | Responsibility |
| --- | --- |
| `Concord.Emit` | Copies IL, lowers injections, and composes method wrappers |
| `Concord.Detour` | Installs wrappers and tracks live injections for each target |
| `Concord.AttachedData` | Stores attached data without changing the target type |
| `Concord.Orchestration` | Provides `Patcher`, patch discovery, the fluent API, and apply or undo behavior |

The `Concord` project merges those libraries into `Concord.dll` and packs `Concord.Runtime`. The analyzer, generator, and reference packages each have their own project under `src/`. `Concord.Harmony` also lives under `src/` and packs on its own, because a host side-loads it rather than getting it from `Concord.dll`.

Tests and benchmarks do not ship in any Concord package. See [Contributing](contributing.md#choose-the-right-project) for the full project map and build instructions.
