# Concord for RimWorld

[![Steam Workshop](https://img.shields.io/badge/Steam_Workshop-Concord-1b2838?logo=steam&logoColor=white)](https://steamcommunity.com/sharedfiles/filedetails/?id=3758333473)
[![Discord](https://img.shields.io/badge/Discord-Cryptiks_Mods-5865F2?logo=discord&logoColor=white)](https://discord.gg/tbcKN8e4mZ)

The RimWorld adapter for [Concord](https://github.com/ConcordLib/Core). It ships the Concord runtime as a RimWorld mod, so any mod can write `[Patch]`/`[Inject]` templates and add saved per-instance state with `[Attached]` fields.

Load it before every mod that depends on it. It sets `loadBefore Ludeon.RimWorld`.

## Supported game version

The mod ships for RimWorld 1.7 only. RimWorld 1.7 runs on WaylithCLR at `net10.0`, and Concord patches through that runtime's own pipeline.

## What the mod ships

The Concord runtime loads from `1.7/Assemblies/`, which RimWorld scans for mod assemblies. The host adapter sits in `1.7/Backend/` instead, outside that scan. The mod loads the adapter itself at startup and asks it to install the patch pipeline.
