# Shadowline

Web tool for comparing lunar landing sites and dates: Sun and Earth position against the local horizon, illumination and Earth-visibility windows, and plan robustness: what happens when the launch slips, the lander misses the point or tips over.

Sections: Explore (`/`), Plan (`/plan/where`, `/plan/when`, `/plan/sky`, `/plan/tipover`, report), How it works (`/science/method`, `/science/validation`, `/science/landings`, `/science/about`), Sources (`/sources`).

NASA Space Apps Challenge 2026, challenge "Lunar Landing Site Navigator".

## Stack

.NET 10, Blazor WebAssembly, static hosting. Offline data preparation in Python.

## Build

```
dotnet build
dotnet test
dotnet run --project src/Shadowline.Web
```

## Publish

```
publish.cmd
```

Runs the tests, publishes the site to `deploy/publish/wwwroot` (old output is removed first) and prints the upload command. Options: `tools/publish.ps1 -SkipTests`, `-Output <dir>`.

## Models and data

Data files in `src/Shadowline.Web/wwwroot/data` are built offline by the scripts in `tools/`. Time grid, SPICE kernels, ephemeris, validation, horizons and terrain are run in order by `tools/pipeline.py`. Both ML models are retrained with one command, see `tools/ml/README.md`:

```
pip install -r tools/ml/requirements.txt
python tools/ml/train_all.py --dem LDEM_80S_80MPP_ADJ.TIF --prp dlre_prp_south.tab
```

Method, metrics and errors of every number on the site: `/science/method`. Checks against JPL Horizons and LRO Diviner: `/science/validation`. Data, software and research used: `/sources`.

## Layout

```
src/Shadowline.Core      domain types and computation, no IO
src/Shadowline.Data      readers and validators for data files
src/Shadowline.Web       Blazor WebAssembly UI
src/Shadowline.Tests     xUnit tests
tools/                   Python and C# data and training tools
```
