# Concord Docs

Documentation site for [Concord](https://github.com/ConcordLib/Concord): the docfx sources,
theme, hand-written API overwrite pages, and the Cloudflare Worker that serves the versioned
docs from R2.

## Layout

Paths are relative to this directory.

- `docs/`: conceptual documentation (getting started, patch model, attached data, and so on)
- `apidoc/`: hand-authored `uid:` overwrite pages layered onto generated API metadata
- `templates/concord/`: the docfx theme (Tailwind build, layout, assets)
- `worker/`: Cloudflare Worker serving versioned docs from the `concord-docs` R2 bucket
- `docfx.json`, `index.md`, `toc.yml`: docfx entry points
- `../.github/workflows/docs.yml`: build, upload to R2, deploy the worker

## Build

Run these commands from this directory.

```bash
dotnet tool install -g docfx
docfx build docfx.json      # conceptual pages, output in .site/
```

`docfx metadata` reads the projects under `../src/core` and `../src/backends`. The backend projects
reference the merged `Concord.dll`. Build it before you generate metadata. Run a full build with:

```bash
dotnet build ../src/core/Concord/Concord.csproj -c Release
docfx metadata docfx.json   # generated metadata in api/
docfx build docfx.json      # complete site in .site/
```
