import argparse
import csv
import json
from datetime import datetime, timezone
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage
from sklearn.ensemble import HistGradientBoostingRegressor


def stats(model, measured):
    d = model - measured
    r = None if np.std(model) < 1e-6 or np.std(measured) < 1e-6 else round(float(np.corrcoef(model, measured)[0, 1]), 3)
    return {"count": int(len(d)), "biasK": round(float(d.mean()), 2), "rmseK": round(float(np.sqrt((d ** 2).mean())), 2), "maeK": round(float(np.abs(d).mean()), 2), "r": r}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", required=True)
    parser.add_argument("--index", required=True)
    parser.add_argument("--diviner", required=True)
    parser.add_argument("--scene-dir", required=True)
    parser.add_argument("--data-dir", required=True)
    parser.add_argument("--half", type=float, default=304000.0)
    args = parser.parse_args()
    rows_csv = list(csv.DictReader(open(args.model)))
    idx = np.load(args.index)
    div = np.load(args.diviner)
    order = np.array([int(r["index"]) for r in rows_csv])
    rr = idx["rows"][order]
    cc = idx["cols"][order]
    f = lambda key: np.array([float(r[key]) for r in rows_csv])
    lat = f("lat")
    model_max, model_avg, simple_max = f("max_k"), f("mean2cm_k"), f("simple_max_k")
    flat_max, flat_avg = f("flat_max_k"), f("flat_mean2cm_k")
    cell_slope, cell_aspect = f("slope_deg"), f("aspect_deg")
    meas_max, meas_avg = div["max"][rr, cc], div["avg"][rr, cc]
    light = np.asarray(Image.open(Path(args.scene_dir) / "pole_light.png").convert("RGB"), dtype=np.float32)
    sun = light[rr, cc, 0] / 255
    dark = light[rr, cc, 2] > 127
    n = light.shape[0]
    slope_full = np.asarray(Image.open(Path(args.scene_dir) / "pole_slope.jpg").convert("L").resize((n, n), Image.BOX), dtype=np.float32) / 4
    slope = slope_full[rr, cc]
    classes = {"all": np.ones(len(lat), bool), "neverSunlit": dark, "sunlitUnder20": (~dark) & (sun < 0.2), "sunlit20to50": (sun >= 0.2) & (sun < 0.5), "sunlitOver50": sun >= 0.5}
    result = {"max": {}, "avg2cm": {}, "flatMax": {}, "flatAvg2cm": {}, "simpleMax": {}}
    for name, mask in classes.items():
        if mask.sum() < 5:
            continue
        result["max"][name] = stats(model_max[mask], meas_max[mask])
        result["avg2cm"][name] = stats(model_avg[mask], meas_avg[mask])
        result["simpleMax"][name] = stats(simple_max[mask], meas_max[mask])
        result["flatMax"][name] = stats(flat_max[mask], meas_max[mask])
        result["flatAvg2cm"][name] = stats(flat_avg[mask], meas_avg[mask])

    x = (cc + 0.5) * 2 * args.half / n - args.half
    y = args.half - (rr + 0.5) * 2 * args.half / n
    block = (np.floor((x + args.half) / 50000) * 100 + np.floor((y + args.half) / 50000)).astype(int)
    blocks = np.unique(block)
    rng = np.random.default_rng(3)
    fold_of = {b: k for k, b in enumerate(rng.permutation(blocks))}
    fold = np.array([fold_of[b] % 5 for b in block])
    features = np.stack([model_max, model_avg, sun, cell_slope, np.cos(np.radians(cell_aspect)), np.sin(np.radians(cell_aspect)), slope, lat, dark.astype(float)], axis=-1)
    ml = {}
    offset = {}
    for target, measured, physics in (("max", meas_max, model_max), ("avg2cm", meas_avg, model_avg)):
        predicted = np.zeros_like(measured)
        shifted = np.zeros_like(measured)
        for k in range(5):
            train, test = fold != k, fold == k
            reg = HistGradientBoostingRegressor(max_depth=4, max_iter=300, learning_rate=0.05, random_state=1)
            reg.fit(features[train], measured[train] - physics[train])
            predicted[test] = physics[test] + reg.predict(features[test])
            shifted[test] = physics[test] + float(np.mean(measured[train] - physics[train]))
        ml[target] = {name: stats(predicted[mask], measured[mask]) for name, mask in classes.items() if mask.sum() >= 5}
        offset[target] = {name: stats(shifted[mask], measured[mask]) for name, mask in classes.items() if mask.sum() >= 5}

    pick = rng.choice(len(lat), size=min(800, len(lat)), replace=False)
    scatter = [[round(float(model_max[i]), 1), round(float(meas_max[i]), 1), round(float(model_avg[i]), 1), round(float(meas_avg[i]), 1), int(dark[i])] for i in pick]
    card = {
        "formatVersion": 1,
        "builtAtUtc": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "reference": "LRO Diviner Polar Resource Product LRO-L-DLRE-5-PRP-V2.0, south (Paige et al. 2010): annual maximum surface temperature and annual average temperature 2 cm below the surface, both calculated by a thermal model fitted to Diviner observations",
        "model": "Regolith1D after Hayne et al. 2017, two years of spin-up over the shipped ephemeris year, then one year of output, horizon from the 1 km pole map (240 m DEM), surface tilted by the 480 m slope and aspect from LDEM_80S_80MPP_ADJ; flat ground for comparison",
        "cells": int(len(lat)),
        "regolith": result,
        "mlCorrection": {"method": "Gradient-boosted residual on top of the physics model, features: physics max and mean, yearly Sun share, slope and aspect at 480 m, mean slope in the cell, latitude, never-sunlit flag; 5-fold cross-validation over 50 km blocks", "results": ml, "offsetBaseline": offset},
        "scatter": {"columns": ["modelMaxK", "divinerMaxK", "modelAvg2cmK", "divinerAvg2cmK", "neverSunlit"], "rows": scatter},
    }
    Path(args.data_dir, "thermal_validation.json").write_text(json.dumps(card, indent=1), encoding="utf-8")
    print(json.dumps({"max": result["max"], "avg": result["avg2cm"], "simple": result["simpleMax"]["all"], "ml": ml}, indent=1))


if __name__ == "__main__":
    main()
