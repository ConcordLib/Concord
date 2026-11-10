# Concord Docs

Documentation site for [Concord](https://github.com/ConcordLib/Core): the docfx sources,
theme, hand-written API overwrite pages, and the Cloudflare Worker that serves the versioned
docs from R2.

## Layout

- `docs/`: conceptual documentation (getting started, patch model, attached data, and so on)
- `apidoc/`: hand-authored `uid:` overwrite pages layered onto generated API metadata
- `templates/concord/`: the docfx theme (Tailwind build, layout, assets)
- `worker/`: Cloudflare Worker serving versioned docs from the `concord-docs` R2 bucket
- `docfx.json`, `index.md`, `toc.yml`: docfx entry points
- `.github/workflows/deploy-docs.yml`: build, upload to R2, deploy the worker

## Build

```bash
dotnet tool install -g docfx
docfx build docfx.json      # conceptual pages, output in .site/
```

`docfx metadata` expects Core projects under a local `src/` directory. The deploy workflow stages
`ConcordLib/Core` there before generating API metadata. After staging Core, run a full build with:

```bash
docfx metadata docfx.json   # generated metadata in api/
docfx build docfx.json      # complete site in .site/
```
