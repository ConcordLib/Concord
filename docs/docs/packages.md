# Packages

Concord publishes four NuGet packages. The package you need depends on what you are building.

Use the same version for every Concord package in a project. The examples on this page use `0.7.0`.

## Choose what to reference

| Project | Required | Optional |
| --- | --- | --- |
| A mod for a runtime that already loads Concord | `Concord.Ref` | `Concord.Analyzers` and `Concord.Generators` |
| A runtime adapter or host | `Concord.Runtime` | None |
| Concord itself | The projects in the Core repository | Analyzer and generator projects as needed |

Mods compile against `Concord.Ref`. The target runtime loads the implementation from `Concord.dll`, so a mod should not reference or ship `Concord.Runtime`.

## Mod projects

### Concord.Ref

`Concord.Ref` is a metadata-only reference assembly. It gives the compiler and IDE access to Concord's public API, including `[Patch]`, `[Inject]`, `ControlHandle<T>`, `Patcher`, and `AttachedField<,>`.

Add it to the mod project:

```xml
<ItemGroup>
  <PackageReference Include="Concord.Ref" Version="0.7.0" />
</ItemGroup>
```

The package contains no runtime implementation. Its assembly identity matches the Concord Assembly, so code compiled against `Concord.Ref` binds to the `Concord.dll` loaded by the target runtime.

`Concord.Ref` provides `net10.0` and `netstandard2.0` assets. A .NET Framework 4.7.2 mod can compile against the `netstandard2.0` asset.

### Optional build tools

Patches compile and run without the analyzer or generator packages. Add either tool when you want the checks or generated code it provides. Keep `PrivateAssets="all"` so it stays in the build and does not become a dependency of the mod.

```xml
<ItemGroup>
  <PackageReference Include="Concord.Analyzers" Version="0.7.0" PrivateAssets="all" />
  <PackageReference Include="Concord.Generators" Version="0.7.0" PrivateAssets="all" />
</ItemGroup>
```

#### Concord.Analyzers

`Concord.Analyzers` reports patch mistakes in the compiler and IDE. It checks target names, injection signatures, injected members, control and operation handles, and patch ordering when it can resolve the target from the project.

It also suppresses field-use warnings for valid `[InjectField]` declarations and suggests compiler-checked forms such as `typeof` and `nameof` when they are available.

#### Concord.Generators

`Concord.Generators` creates a patch registry for the assembly. `Patcher.Apply` uses that registry instead of scanning every type through reflection.

The package can also generate typed `[InjectField]`, `[InjectProperty]`, and `[InjectMethod]` members from `[Shadow]` declarations. Its IDE refactorings can create patches, injections, and shadow members.

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
  <PackageReference Include="Concord.Runtime" Version="0.7.0" />
</ItemGroup>
```

The package provides `net10.0`, `netstandard2.0`, and `net472` assemblies. It has no NuGet dependencies because the build folds the four runtime libraries and their MonoMod dependencies into each `Concord.dll`.

The package ID is `Concord.Runtime` because the plain `Concord` ID on NuGet belongs to an unrelated package.

## Core contributors

The Core repository builds the runtime from four internal projects:

| Project | Responsibility |
| --- | --- |
| `Concord.Emit` | Copies IL, lowers injections, and composes method wrappers |
| `Concord.Detour` | Installs wrappers and tracks live injections for each target |
| `Concord.AttachedData` | Stores attached data without changing the target type |
| `Concord.Orchestration` | Provides `Patcher`, patch discovery, the fluent API, and apply or undo behavior |

The `Concord` project merges those libraries into `Concord.dll` and packs `Concord.Runtime`. The analyzer, generator, and reference packages each have their own project under `src/`.

Tests and benchmarks do not ship in any Concord package. See [Contributing](contributing.md#choose-the-right-project) for the full project map and build instructions.
