import argparse
import hashlib
import json
import math
import struct
from datetime import datetime, timezone
from pathlib import Path

import numpy as np
import PIL
import scipy
import sklearn
from PIL import Image
from scipy import ndimage
from sklearn.ensemble import HistGradientBoostingClassifier
from sklearn.inspection import permutation_importance
from sklearn.linear_model import LogisticRegression
from sklearn.metrics import roc_auc_score
from sklearn.pipeline import make_pipeline
from sklearn.preprocessing import PolynomialFeatures, StandardScaler

MOON_RADIUS_M = 1737400.0
MONOTONIC = [1, 1, 1, -1, -1, 0, 0]
HALF_M = 304000.0
DOMAIN_LAT = -83.0
LOCAL_KM = 25.0
EXCLUDE_KM = 2.0

FEATURES = [
    ("sun", "Share of the year with the Sun up"),
    ("earth", "Share of the year with Earth up"),
    ("sun_max_3km", "Best Sun share within 3 km"),
    ("psr_km", "Distance to the nearest area with no Sun, log km"),
    ("slope", "Mean slope in the 1 km cell, degrees"),
    ("elevation", "Elevation, km"),
    ("relief", "Height spread within 5 km, km"),
]


BASELINES = [("elevation", 5, 1), ("sun", 0, 1), ("sun_max_3km", 2, 1), ("slope", 4, -1)]


def md5(path):
    return hashlib.md5(Path(path).read_bytes()).hexdigest()


def polar_xy(lat, lon):
    rho = 2 * MOON_RADIUS_M * math.tan(math.radians(90 + lat) / 2)
    return rho * math.sin(math.radians(lon)), rho * math.cos(math.radians(lon))


def load_grids(scene, n):
    light = np.asarray(Image.open(scene / "pole_light.png").convert("RGB"), dtype=np.float32)
    if light.shape[0] != n:
        raise SystemExit("pole_light.png size differs")
    sun = light[..., 0] / 255
    earth = light[..., 1] / 255
    dark = light[..., 2] > 127
    slope_img = Image.open(scene / "pole_slope.jpg").convert("L").resize((n, n), Image.BOX)
    slope = np.asarray(slope_img, dtype=np.float32) / 4
    raw = (scene / "pole_height.bin").read_bytes()
    m, half, unit = struct.unpack("<iff", raw[:12])
    heights = np.frombuffer(raw, dtype="<i2", offset=12, count=m * m).reshape(m, m).astype(np.float32) * unit
    zoom = n / m
    elevation = ndimage.zoom(heights, zoom, order=1)[:n, :n] / 1000
    relief = ndimage.generic_filter(elevation, np.std, size=5, mode="nearest")
    psr_km = ndimage.distance_transform_edt(~dark) * (2 * HALF_M / n / 1000)
    sun_max = ndimage.maximum_filter(sun, size=7, mode="nearest")
    stack = np.stack([sun, earth, sun_max, np.log1p(np.minimum(psr_km, 50)), slope, elevation, relief], axis=-1)
    return stack


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--data-dir", required=True)
    parser.add_argument("--scene-dir", required=True)
    parser.add_argument("--n", type=int, default=608)
    args = parser.parse_args()
    data = Path(args.data_dir)
    scene = Path(args.scene_dir)
    n = args.n
    cell = 2 * HALF_M / n
    grid = load_grids(scene, n)

    j = np.arange(n)
    xs = -HALF_M + (j + 0.5) * cell
    ys = HALF_M - (j + 0.5) * cell
    xx, yy = np.meshgrid(xs, ys)
    rho = np.hypot(xx, yy)
    lat = -90 + np.degrees(2 * np.arctan(rho / (2 * MOON_RADIUS_M)))
    domain = lat <= DOMAIN_LAT

    artemis = json.loads((data / "artemis.json").read_text(encoding="utf-8"))
    sites = []
    current = {region["code"] for region in artemis["regions"] if region.get("artemis3")}
    for region in artemis["regions"]:
        for site in region["sites"]:
            x, y = polar_xy(site["lat"], site["lon"])
            col = int((x + HALF_M) / cell)
            row = int((HALF_M - y) / cell)
            sites.append((region["code"], row, col, x, y))
    site_lat = np.array([site["lat"] for region in artemis["regions"] for site in region["sites"]])
    if (site_lat > DOMAIN_LAT).any():
        raise SystemExit("a site lies outside the score domain")

    positive = np.zeros((n, n), dtype=bool)
    for _, row, col, _, _ in sites:
        positive[row, col] = True
    near_positive = ndimage.binary_dilation(positive, iterations=int(EXCLUDE_KM * 1000 / cell))
    background = domain & ~near_positive

    pos_rows = np.array([s[1] for s in sites])
    pos_cols = np.array([s[2] for s in sites])
    pos_regions = np.array([s[0] for s in sites])
    X_pos = grid[pos_rows, pos_cols]
    bg_idx = np.argwhere(background)
    X_bg = grid[bg_idx[:, 0], bg_idx[:, 1]]
    bg_xy = np.stack([xx[background], yy[background]], axis=-1)

    def models():
        return {
            "logistic": make_pipeline(StandardScaler(), PolynomialFeatures(2, include_bias=False), StandardScaler(), LogisticRegression(C=0.5, class_weight="balanced", max_iter=4000)),
            "boosting": HistGradientBoostingClassifier(max_depth=3, max_iter=200, learning_rate=0.05, class_weight="balanced", random_state=1),
            "monotonic": HistGradientBoostingClassifier(max_depth=3, max_iter=200, learning_rate=0.05, class_weight="balanced", random_state=1, monotonic_cst=MONOTONIC),
        }

    pos_xy = np.array([[s[3], s[4]] for s in sites])
    folds = []
    scores = {name: {"local": []} for name in models()}
    baseline_scores = {name: [] for name, _, _ in BASELINES}
    current_scores = []
    held_importance = []
    for code in sorted(set(pos_regions)):
        test_pos = pos_regions == code
        centre = pos_xy[test_pos].mean(axis=0)
        dist = np.hypot(bg_xy[:, 0] - centre[0], bg_xy[:, 1] - centre[1]) / 1000
        pos_dist = np.hypot(pos_xy[:, 0] - centre[0], pos_xy[:, 1] - centre[1]) / 1000
        local = dist <= LOCAL_KM
        train_bg = dist > LOCAL_KM
        train_pos = ~test_pos & (pos_dist > LOCAL_KM)
        X_train = np.vstack([X_pos[train_pos], X_bg[train_bg]])
        y_train = np.concatenate([np.ones(train_pos.sum()), np.zeros(train_bg.sum())])
        X_test = np.vstack([X_pos[test_pos], X_bg[local]])
        y_test = np.r_[np.ones(test_pos.sum()), np.zeros(local.sum())]
        fold = {"region": code, "current": code in current, "sites": int(test_pos.sum()), "localBackground": int(local.sum()), "neighbourSitesDropped": int((~test_pos & ~train_pos).sum())}
        for name, model in models().items():
            model.fit(X_train, y_train)
            auc_local = roc_auc_score(y_test, model.predict_proba(X_test)[:, 1]) if local.sum() > 0 else float("nan")
            scores[name]["local"].append(auc_local)
            fold[name] = {"localAuc": round(auc_local, 3)}
            if name == "monotonic":
                if code in current:
                    current_scores.append(auc_local)
                held = permutation_importance(model, X_test, y_test, scoring="roc_auc", n_repeats=5, random_state=1)
                held_importance.append(held.importances_mean)
        for name, column, sign in BASELINES:
            auc = roc_auc_score(y_test, sign * X_test[:, column])
            baseline_scores[name].append(auc)
            fold.setdefault("baselines", {})[name] = round(auc, 3)
        folds.append(fold)
        print(code, fold)

    summary = {name: {"meanAuc": round(float(np.nanmean(s["local"])), 3), "minAuc": round(float(np.nanmin(s["local"])), 3), "maxAuc": round(float(np.nanmax(s["local"])), 3)} for name, s in scores.items()}
    baselines = {name: {"meanAuc": round(float(np.mean(v)), 3), "minAuc": round(float(np.min(v)), 3)} for name, v in baseline_scores.items()}
    current_summary = {"regions": len(current_scores), "meanAuc": round(float(np.mean(current_scores)), 3), "minAuc": round(float(np.min(current_scores)), 3)}
    held_mean = np.mean(held_importance, axis=0)
    print(summary, baselines, current_summary)
    best = "monotonic"
    final = models()[best]
    X_all = np.vstack([X_pos, X_bg])
    y_all = np.concatenate([np.ones(len(X_pos)), np.zeros(len(X_bg))])
    final.fit(X_all, y_all)

    flat = grid.reshape(-1, grid.shape[-1])
    prob = final.predict_proba(flat)[:, 1].reshape(n, n)
    ranks = np.zeros_like(prob)
    inside = prob[domain]
    order = inside.argsort().argsort()
    ranks[domain] = (order + 1) / len(inside)
    out = np.where(domain, np.clip(np.round(ranks * 254) + 1, 1, 255), 0).astype(np.uint8)
    (data / "pole_artemis.bin").write_bytes(struct.pack("<iff", n, HALF_M, DOMAIN_LAT) + out.tobytes())

    site_pct = [float(ranks[r, c]) for r, c in zip(pos_rows, pos_cols)]
    card = {
        "formatVersion": 1,
        "name": "Artemis likeness",
        "builtAtUtc": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "task": "Presence-background classification: the published candidate landing sites inside NASA's Artemis regions (Wueller et al. 2026, an independent study, not a NASA selection) against all other 1 km cells south of 83 S",
        "labels": {"source": artemis["source"], "announcement": artemis["announcement"], "regions": len(set(pos_regions)), "currentRegions": len(current), "sites": len(sites), "sitesInCurrentRegions": int(np.isin(pos_regions, list(current)).sum()), "backgroundCells": int(len(X_bg))},
        "features": [{"id": f, "description": d, "importance": round(float(held_mean[k]), 4)} for k, (f, d) in enumerate(FEATURES)],
        "importanceNote": "Permutation importance (drop in AUC) on the held-out sites and local background of each fold, averaged over the folds",
        "model": best,
        "modelNote": "Gradient-boosted trees (depth 3, 200 rounds) with monotonic constraints: the score can only rise with Sun share, Earth share and best Sun within 3 km, and only fall with slope and with distance to areas with no Sun (closeness to permanent shadow is rewarded on purpose, for access to cold traps); elevation and relief are free. Logistic regression and unconstrained trees were trained the same way for comparison",
        "models": summary,
        "baselines": baselines,
        "currentRegionFolds": current_summary,
        "validation": {"method": "Leave one region out: train without that region, without the background within 25 km of its centre and without sites of other regions within 25 km of it, then score its sites against that local background. AUC 0.5 is chance, 1.0 is perfect. Baselines rank the same test cells by one feature with no training", "folds": folds},
        "reproduce": {"python": __import__("sys").version.split()[0], "numpy": np.__version__, "scipy": scipy.__version__, "scikit-learn": sklearn.__version__, "Pillow": PIL.__version__, "inputs": {name: md5(scene / name) for name in ("pole_light.png", "pole_slope.jpg", "pole_height.bin")} | {"artemis.json": md5(data / "artemis.json")}},
        "output": {"file": "data/pole_artemis.bin", "meaning": "Percentile of the model score among cells south of 83 S, 1-255, 0 outside", "sitesMedianPercentile": round(float(np.median(site_pct)), 3)},
    }
    (data / "ml_artemis.json").write_text(json.dumps(card, indent=2), encoding="utf-8")
    print(json.dumps(card["models"], indent=2), card["output"], [ (f["id"], f["importance"]) for f in card["features"]])


if __name__ == "__main__":
    main()
