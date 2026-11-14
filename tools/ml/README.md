# Model training

Two models are trained offline. The site only reads their output files.

Model descriptions (data, preprocessing, features, validation, results, limits) are in `docs/ML_ARTEMIS.md` and `docs/ML_THERMAL.md`. Source labels are in `tools/artemis/source/`, intermediate thermal data in `tools/thermal/data/`.

| Model | Script | Output | Check |
|---|---|---|---|
| Artemis likeness | `train_artemis.py` | `data/pole_artemis.bin`, `data/ml_artemis.json` | leave one region out, ROC AUC |
| Regolith residual against Diviner | `../thermal/validate_regolith.py` | `data/thermal_validation.json` | 5-fold CV over 50 km blocks, RMSE |

`train_all.py` runs every step in order. Paths in this file are relative to the repository root.

## Setup (Windows, PowerShell)

Python 3.10 or newer and the .NET 10 SDK.

```
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r tools/ml/requirements.txt
```

## Input files

| File | Size | Source | Needed for |
|---|---|---|---|
| `LDEM_80S_80MPP_ADJ.TIF` | 181 MB | https://pgda.gsfc.nasa.gov/data/LOLA_20mpp/LDEM_80S_80MPP_ADJ.TIF (Barker et al. 2021) | horizons, slopes |
| `dlre_prp_south.tab` | 605 MB | https://pds-geosciences.wustl.edu/lro/urn-nasa-pds-lro_diviner_derived1/data_derived_prp/dlre_prp_south.tab (Paige et al. 2010) | diviner |

Everything else is in the repository (`data/` and `img/` under `src/Shadowline.Web/wwwroot`): `data/artemis.json`, `data/manifest.json`, `data/ephemeris.bin`, `img/scene/pole_light.png`, `img/scene/pole_slope.jpg`, `img/scene/pole_height.bin`.

`dlre_prp_south.tab` has 2 880 000 records of 210 bytes plus a header line, 604 800 210 bytes in total. `tools/thermal/check_prp.py --prp dlre_prp_south.tab` checks size, line ends, characters and value ranges and prints the md5 (80cdfe83592a3419c74b1b55b636711e for the copy used here).

## Run everything

```
python tools/ml/train_all.py --dem D:\data\LDEM_80S_80MPP_ADJ.TIF --prp D:\data\dlre_prp_south.tab
```

Intermediate files go to `data-cache/ml` (ignored by git), `--work` changes it. `--steps` picks a subset, for example `--steps artemis` needs no downloads. `--jobs` sets parallel RegolithCheck processes (default 4), `--cells` the number of check cells (default 4000).

| Step | What it does |
|---|---|
| `artemis` | features from the pole maps, 16 folds for 3 models, final fit, writes the score map and model card |
| `diviner` | checks the PRP file, grids it to 608 x 608 cells, writes `data/pole_diviner.bin` and `diviner_grid.npz` |
| `horizons` | only if `hz.npz` is missing: horizons of the 1 km pole grid from the DEM, the slowest step |
| `cells` | 4000 random cells with their horizons to `cells.bin` |
| `slopes` | slope and aspect over 480 m for each cell from the DEM to `slopes.bin` |
| `regolith` | builds `tools/thermal/RegolithCheck`, runs the same `Regolith1D` code as the site for a year per cell, writes `model.csv` |
| `validate` | compares with Diviner, trains the residual model, writes `data/thermal_validation.json` |

## Artemis likeness

Labels: 130 candidate sites from Wueller et al. (2026), doi:10.5281/zenodo.17084058, an independent study, not a NASA selection. They lie in 16 regions: the 9 of NASA's October 2024 list (78 sites) and 7 of the 2022 list that NASA dropped (52 sites). Background: every 1 km cell south of 83 S further than 2 km from a site.

Features per cell: yearly Sun share, yearly Earth share, best Sun share within 3 km, log distance to the nearest cell without Sun, slope, elevation, height spread within 5 km.

Models: quadratic logistic regression, gradient-boosted trees, gradient-boosted trees with monotonic constraints (kept). Trees: depth 3, 200 rounds, learning rate 0.05, balanced class weights.

Check: for each region, train without its sites, without background within 25 km of its centre and without sites of other regions within 25 km, then compute ROC AUC of its sites against that local background. Single-feature baselines (elevation, Sun share, best Sun within 3 km, slope) are scored on the same cells. Permutation importance is computed on the held-out cells of each fold. The final model is fitted on all data. Fixed seeds and pinned versions in `requirements.txt`; the model card records versions and input md5.

## Regolith residual

Target: Diviner annual maximum surface temperature and annual mean 2 cm below the surface minus the `Regolith1D` result for the same cell.

Features: model maximum and 2 cm mean, yearly Sun share, slope and aspect over 480 m, mean slope in the 1 km cell, latitude, never-sunlit flag.

Model: gradient-boosted regression trees, depth 4, 300 rounds, learning rate 0.05.

Check: cells are grouped in 50 x 50 km squares, the squares are split into 5 folds, each fold is predicted by a model trained on the other four. Bias, RMSE, MAE and Pearson r are reported for the physics model alone and with the residual, overall and by lighting class.

The residual model is a measure of the physics error. The site does not apply it; reports show Diviner values where they exist.
