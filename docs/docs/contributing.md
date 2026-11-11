# Contributing

Concord lives in one repository, [ConcordLib/Concord](https://github.com/ConcordLib/Concord). It contains the runtime, packages, analyzers, generators, and tests. This site and its theme live under `docs/`.

Change code under `src/` and `tests/`. Change guides, API notes, and the site under `docs/`. A change to public behavior often needs updates in both.

## Core development

### Set up the repository

The build requires the .NET 10 SDK. The `global.json` file accepts the latest installed .NET 10 feature band.

```bash
git clone https://github.com/ConcordLib/Concord.git
cd Concord
dotnet restore Concord.ci.slnx
dotnet build Concord.ci.slnx -c Release --no-restore
dotnet test Concord.ci.slnx -c Release --no-restore -f net10.0
```

The build treats warnings as errors. Run the affected test project while you work, then run the CI solution before you push.

CI tests the runtime libraries on .NET Framework 4.7.2. Windows runs those tests through .NET Framework, while Linux uses Mono. Changes to Emit, Detour, AttachedData, or Orchestration must keep that target working.

### Choose the right project

The repository keeps each runtime job in a separate project. Runtime libraries live under `src/core`, and backends live under `src/backends`. The `Concord` project merges the five runtime libraries into one `Concord.dll` during packaging.

| Project | Change it for |
| --- | --- |
| `Concord.Emit` | IL copying, injection positions, control handles, operation handles, or wrapper composition |
| `Concord.Backend` | The backend contract, injection ordering, target verdicts, or method identity keys |
| `Concord.Enums` | Extended enum members and their registry |
| `Concord.Orchestration` | `Patcher`, the fluent builder, patch discovery, declaration scanning, or apply and undo behavior |
| `Concord.AttachedData` | Weak-reference storage used by attached fields |
| `Concord.Analyzers` | Compiler diagnostics for patch declarations |
| `Concord.Generators` | Patch registries, generated shadow members, or IDE refactorings |
| `Concord.Ref` | Reference-package construction and public API verification |
| `Concord` | Merging and packaging the runtime assembly |
| `Concord.MonoMod` | Emitting composed bodies on .NET Framework and Mono, or the Harmony bridge |
| `Concord.HarmonyX` | Patching on HarmonyX hosts such as BepInEx |
| `Concord.Waylith` | Staging composed bodies on the WaylithCLR runtime |

Tests follow the same layout under `tests/`. For example, changes in `Concord.Emit` belong with tests in `Concord.Emit.Tests`.

## Runtime architecture

### How a patch reaches the runtime

1. `Patcher.Apply` gets patch declarations from the generated registry. It scans the assembly when no registry exists.
2. `PatchDeclarationScanner` reads `[Patch]`, `[Inject]`, and related attributes. It creates an `Injection` for each resolved injection method.
3. `CollectingPatchApplier` sends each target and injection to `IPatchBackend.ApplyComposed`.
4. The backend keeps the live injections for that target. `InjectionOrderer` resolves priority and before or after rules.
5. `WrapperComposer` builds a wrapper from the target body and the ordered injections. The backend installs that wrapper on the target method.
6. Disposing the returned handle removes its injections. Concord rebuilds the wrapper for any injections that remain, or removes the detour when the target has none.

For a runtime bug, find the last correct value in that sequence and follow the next call. Keep the existing boundaries between layers.

### Composition code

`WrapperComposer` coordinates composition. `BodyCopier` copies method bodies, arguments, locals, branches, and exception handlers. `InjectedMemberMap` resolves declaration members, while `ControlHandleLowering` rewrites control calls.

The composer returns a `ComposeResult`. Its `Wrapper` becomes the detour target. Its `OriginalBody` is an unpatched copy of the target body.

`WrapperComposer` throws `ConcordEmitException` with a stable `CONCxxx` code when composition fails. `PatchDeclarationScanner` throws `ConcordDeclarationException` when it cannot resolve a patch. Add or update an analyzer rule as part of the change if the compiler can detect the same mistake.

### Public API changes

`Concord.Ref` compiles the source files from the five runtime libraries into a metadata-only assembly. Compiling the same source keeps its public surface tied to the runtime implementation.

Build the reference package after changing a public type or member:

```bash
dotnet build src/core/Concord.Ref/Concord.Ref.csproj -c Release --no-restore
```

Keep MonoMod types out of public signatures. Add XML documentation for public APIs, and update the user docs when behavior changes.

### Design rules

- Keep the author API close to ordinary C#.
- Put IL work in Emit and live runtime state in Detour.
- Keep runtime validation and analyzer diagnostics consistent when the compiler has enough information.
- Check invalid input before touching an active detour.

## Verification

### Tests

Run the smallest relevant suite while you work:

```bash
dotnet test tests/Concord.Emit.Tests/Concord.Emit.Tests.csproj -f net10.0
dotnet test tests/Concord.Backend.Tests/Concord.Backend.Tests.csproj -f net10.0
dotnet test tests/Concord.Orchestration.Tests/Concord.Orchestration.Tests.csproj -f net10.0
```

Add a regression test for a bug. For ordering, use operations whose result changes when the order changes. Exercise apply and dispose when a registry change affects recomposition. Analyzer tests compile short source samples and assert the diagnostic ID and location.

Benchmarks live under `bench/Concord.Benchmarks`. Run them when you change code on the patched method's hot path or the cost of applying many patches.

### Documentation changes

The site builds with DocFX from the `docs/` directory:

```bash
cd docs
docfx build docfx.json
```

DocFX writes the generated site to `.site/`. It may print existing link warnings from the custom home page. Read each warning and make sure your change did not add one.

Theme changes need the Tailwind build first:

```bash
cd docs/templates/concord
npm ci
npm run build:css
```

Write docs for the current API. Leave upgrade notes and old-versus-new comparisons to pages that cover a migration.

## Writing documentation

The site follows the [Google developer documentation style guide](https://developers.google.com/style)
for vocabulary and structure. On top of that it applies the mechanical rules of ASD-STE100:

- Keep sentences to 25 words or fewer.
- Use active voice. Name who does the action, usually Concord, or the reader.
- Give one instruction per sentence.
- Use no em dashes or en dashes.

Vale checks all of this. Run it from the `docs/` directory before you push:

```bash
vale sync
vale docs/*.md
```

The first command downloads the Google package. You only need it once.

`docs/.vale.ini` turns off four rules that misfire on this content, such as the one that reads
`Concord.Ref` as two sentences. Each entry says why. Turn off a rule only when you can show it
produces a false positive.

Put domain terms in `docs/styles/config/vocabularies/Concord/accept.txt`. ASD-STE100 allows technical
names outside its approved word list, and that file records the ones this site uses.

The `lint-prose` job gates the deploy, so prose that fails Vale does not reach the site.

## Issues

The repository does not take pull requests. To report a bug or ask for a feature, open an issue with the [bug report](https://github.com/ConcordLib/Concord/issues/new?template=bug_report.md) or [feature request](https://github.com/ConcordLib/Concord/issues/new?template=feature_request.md) template.

## Before you push

- Build without warnings.
- Run the affected tests and the full CI solution.
- Add tests for changed behavior.
- Update docs for user-facing changes.
- Check that disposing a patch restores the expected state.
