# Concord

<p align="center">
  <img src="https://github.com/ConcordLib/Docs/blob/main/templates/concord/assets/logo.png?raw=true" alt="Concord" width="480">
</p>

Concord lets a mod change target runtime behavior while it is running. No runtime files
touched, no IL to learn. You write C# that looks like it belongs next to the
target code, and Concord handles runtime patch application.

It's a Harmony alternative built for .NET 10, type-safe, zero-allocation on
the hot path.

## A quick taste

Every campfire should run 5 degrees warmer:

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

That's the whole patch. The target code doesn't move.

## Docs

Everything's at **[concordlib.dev](https://concordlib.dev)**:

- [Start Here](https://concordlib.dev/docs/introduction.html), the core ideas
- [Your First Patch](https://concordlib.dev/docs/first-patch.html), a guided walk-through
- [Common Tasks](https://concordlib.dev/docs/common-tasks.html), the patches you'll write most
- [How Patches Work](https://concordlib.dev/docs/patch-model.html), what's happening underneath
- [API Reference](https://concordlib.dev/api/Concord.html), generated from source

## Packages

- `Concord.Ref` — compile-time reference assembly for authoring mods against the Concord API.
- `Concord.Analyzers` — build-time Roslyn analyzers that validate patch declarations.
- `Concord.Generators` — optional source generators + IDE refactorings (patch registry, `[Shadow]` members, scaffolding).
- `Concord` — the runtime assembly a target ships to load and apply patches.

## Where things stand

Concord 1.0 covers the whole patch model: head, tail, return, around, and
invoke injections; `ControlHandle` cancel and return-value control; private target fields;
reverse patches; attached data on target instances; the `[Patch]` declaration
model; and the apply/undo API. The
[Roadmap](https://concordlib.dev/docs/roadmap.html) covers what comes next.

## Building against the fork

The host adapter compiles against the WaylithCLR fork's own framework
assemblies, not stock .NET. `ForkRuntimeDir` names the directory that holds
them. `scripts/fork-refs.sh` resolves it for you, in order: the
`FORK_RUNTIME_DIR` environment variable, `gamecrate refs`, or a local game
build. Fork facts need the fork's native runtime, so they run separately
from the stock unit tests and must print the WaylithCLR version the runner
actually used:

```bash
scripts/fork-refs.sh
dotnet run --project tests/Concord.Waylith.ForkTests/Concord.Waylith.ForkTests.csproj -c Release -f net10.0 -p:ForkRuntimeDir=<dir>
```

CI builds the game image that backs this with the steam image action, so
the fork facts run on a real WaylithCLR build instead of a stub.

## Contributing

PRs are welcome. If you want to dig into the internals, the
[Contributor Architecture](https://concordlib.dev/docs/architecture.html) page
is a good starting point. Target `main`: the
[pull-request checklist](.github/PULL_REQUEST_TEMPLATE.md) will guide you from there.
