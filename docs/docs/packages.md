# Packages

The Concord runtime ships as one merged assembly. Authoring tools ship in separate packages, and the repo is split into several projects. This page explains which package you need.

## Which do I want?

Most people touch one or two of these.

| You are... | You want |
| --- | --- |
| Writing a mod for a target runtime that already runs Concord | `Concord.Ref` to compile, `Concord.Analyzers` for checks, and `Concord.Generators` for generated authoring support |
| Integrating Concord into a target runtime | `Concord.Runtime`, which carries the Concord Assembly (`Concord.dll`) |
| Working on Concord itself | the four core libraries below |

If you are writing a mod, reference `Concord.Ref` at compile time. Add `Concord.Analyzers` and `Concord.Generators` as private build tools. The target runtime's Concord Assembly runs your patches, so you do not ship `Concord.dll` yourself.

## Mod-author packages

### Concord.Ref

A compile-time-only reference assembly. It mirrors Concord's public API (the `[Patch]`/`[Inject]` attributes, `ControlHandle<T>`, `Patcher`, `AttachedField<,>`, and so on) so your mod compiles and gets IntelliSense, but it carries no implementation. The target runtime's Concord Assembly runs your patches. Reference this, not `Concord.dll`, when authoring a mod.

```xml
<PackageReference Include="Concord.Ref" Version="0.6.0" />
```

It targets both `net10.0` and `netstandard2.0`. The `netstandard2.0` build is the floor a net472 game mod (RimWorld) can compile against, and a net10.0 mod resolves the net10.0 build.

### Concord.Analyzers

Build-time Roslyn analyzers for Concord projects. They validate injected member declarations and `[Inject]` methods wherever the patch target is statically resolvable, including `typeof`, inherited, and resolvable string targets. That covers missing or ambiguous injection targets, injection method parameter and `ControlHandle<T>` mismatches, static target misuse, duplicate injections, malformed `[InjectInstance]` declarations, and plain fields that probably should be `[InjectField]` declarations.

They also suppress intentional `[InjectField]` field-use warnings, prefer `typeof`/`nameof`/inherited `[Patch]` declarations when those are available, and provide bootstrap checks for runtime adapters. Add this package beside `Concord.Ref` and mark it private:

```xml
<PackageReference Include="Concord.Analyzers" Version="0.6.0" PrivateAssets="all" />
```

Analyzer packages are tooling only. They improve editor/build feedback and should not be copied into a mod or treated as runtime dependencies.

### Concord.Generators

Optional build-time generators and IDE refactorings for patch declarations. The package emits a registry so `Patcher.Apply` can find `[Patch]` declarations without scanning every type through reflection. It also generates typed `[InjectField]`, `[InjectProperty]`, and `[InjectMethod]` members from `[Shadow]` attributes on partial patch classes.

Its IDE refactorings can create a patch, add an injection, add a shadow member, or convert a class into a patch declaration. Keep the package private:

```xml
<PackageReference Include="Concord.Generators" Version="0.6.0" PrivateAssets="all" />
```

### Concord Assembly (`Concord.Runtime`)

The shipped artifact: a single `Concord.dll` produced by ILRepack-merging the four core libraries into one file. A target runtime loads it, and it supplies the implementation behind everything `Concord.Ref` declares. Build output lands under `Assemblies/<target-framework>/`. This is what a runtime adapter integrates and what a mod binds to at runtime.

It publishes on nuget.org as `Concord.Runtime` (the plain `Concord` id belongs to an unrelated older package). The package carries one merged DLL for each target framework plus its README. It declares no NuGet dependencies:

```xml
<PackageReference Include="Concord.Runtime" Version="0.6.0" />
```

The package carries separate `net10.0`, `netstandard2.0`, and `net472` assemblies. A .NET Framework 4.7.2 runtime loads the `net472` build. Mods still compile against the compatible asset from `Concord.Ref`.

Mods never reference this package. It exists for runtime integrators.

## Core libraries (contributors)

These four are internal. They compile separately but merge into the single `Concord.dll`, so a mod or runtime adapter never references them on their own. They are where the work happens if you are developing Concord.

### Referencing a local Concord checkout

If you are developing a mod against a sibling Concord source checkout before packages are published, use an explicit `Concord` reference plus project references for build order:

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
  <ProjectReference Include="$(ConcordRoot)/src/Concord.Ref/Concord.Ref.csproj"
                    ReferenceOutputAssembly="false"
                    PrivateAssets="all">
    <Private>false</Private>
  </ProjectReference>
  <Analyzer Include="$(ConcordAnalyzerDll)" />
  <Analyzer Include="$(ConcordGeneratorDll)" />
  <ProjectReference Include="$(ConcordRoot)/src/Concord.Analyzers/Concord.Analyzers.csproj"
                    ReferenceOutputAssembly="false"
                    PrivateAssets="all" />
  <ProjectReference Include="$(ConcordRoot)/src/Concord.Generators/Concord.Generators.csproj"
                    ReferenceOutputAssembly="false"
                    PrivateAssets="all" />
</ItemGroup>
```

Build Concord once before loading the mod project in Rider:

```bash
dotnet build ../../Concord/Core/src/Concord.Ref/Concord.Ref.csproj
dotnet build ../../Concord/Core/src/Concord.Analyzers/Concord.Analyzers.csproj
dotnet build ../../Concord/Core/src/Concord.Generators/Concord.Generators.csproj
```

If Rider still shows `using Concord;` or `[Patch]` as unresolved after the first build, reload the project so its design-time model picks up the new DLL paths.

### Concord.Emit

The IL layer. `WrapperComposer` and `BodyCopier` build wrappers from target IL and ordered `Injection` records. This layer also defines the `At` enum, `InjectAt` positions, control-handle lowering, and the `Operation` family. Rejected patches fail with a `CONCxxx` code. The layer depends on MonoMod.Utils for Cecil.

### Concord.Detour

The detour layer. `IDetourBackend.ApplyComposed` applies injections over a target method. Its handle owns those injections. Disposing it removes them, recomposes the wrapper when others remain, and removes the detour when none do. The default backend uses MonoMod.Core.

### Concord.AttachedData

Weak-reference side storage. `AttachedField<TTarget, TValue>` hangs custom data on target instances without adding real fields to their types, keyed by instance and collected when the target is. Backs the attached-data feature.

### Concord.Orchestration

The author-facing API and declaration scanner. `Patcher.Apply(...)` uses a generated patch registry when one exists and falls back to scanning the assembly. The fluent builder comes from `Patcher.For(...)`. `PatchDeclarationScanner` sends `[Inject]` methods to patch application and plain non-static fields to attached-property registration. Depends on Emit and Detour.

## Not shipped

`bench/` (benchmarks) and `tests/` are development-only. Neither ships in `Concord.dll` or in any package a user consumes.

## How they fit together

```text
Concord.Emit ─┐
Concord.Detour ─┤
Concord.AttachedData ─┼─ ILRepack ─> Concord.dll  (the Concord Assembly)
Concord.Orchestration ─┘                  ▲
                                          │ loads
Concord.Ref  (compile against) ───────────┘
Concord.Analyzers   (build-time checks)
Concord.Generators  (registry, shadows, refactorings)
Runtime adapter  (loads the Concord Assembly into the target runtime)
```

See [Contributing](contributing.md#how-a-patch-reaches-the-runtime) for the apply and composition path inside the core libraries.
