"""Run the whole pipeline from the raw recordings to the final model, one step after the other.

    uv run python -m strikefit_ml.pipeline

Steps (each one is a module of this package, and most have a notebook that shows the same step with plots):
    1. Load the recordings                       ingestion.py        (notebook 01)
    2. Check the data                            data_audit.py       (notebook 01)
    3. Clean and resample the data               preprocessing.py    (notebook 02)
    4. Build windows, features and targets       feature_engineering.py (notebook 03)
    5. Split by recording and normalise          datasets.py         (notebook 04)
    6. Score the baselines                       evaluation.py       (notebook 04)
    7. Train the models with cross-validation    models.py, training.py (notebook 05)
    8. Evaluate on the test recordings           evaluation.py       (notebook 06)
    9. Train the final model and export it       export.py           (notebook 06)

All settings come from configs/motion_forecaster.yaml. The final model and its files are written to models/.
"""

from __future__ import annotations

import time

import numpy as np
import pandas as pd

from .data_audit import run_checks
from .datasets import build_windows, check_split, make_splits, norm_x, pooled_test_files, stack
from .evaluation import (
    best_baseline,
    displacement_errors_cm,
    error_table,
    overall,
    pick_windows,
    plot_error_by_horizon,
    plot_trajectories,
    predict_constant_velocity,
    predict_still,
    results_summary,
    verdict_text,
)
from .export import export_onnx, golden_vector, onnx_io_shapes, onnx_ops, parity, save_final_files, write_model_card
from .feature_engineering import N_FEATURES
from .ingestion import list_recordings, load_config, load_recording, recording_stem, resolve
from .models import build_model, n_params
from .preprocessing import build_interim
from .training import (
    cross_validate,
    final_refit,
    pick_device,
    pick_final_model,
    plot_training_curves,
    pooled_predictions,
    summarise_histories,
)


def step(number: int, title: str) -> None:
    print(f"\nStep {number}/9: {title}")


def run() -> None:
    cfg = load_config()
    models_dir, reports_dir, figures_dir = resolve(cfg, "models_dir"), resolve(cfg, "reports_dir"), resolve(cfg, "figures_dir")
    for folder in (models_dir, reports_dir, figures_dir):
        folder.mkdir(parents=True, exist_ok=True)
    horizons, rate = cfg["horizons_frames"], cfg["sample_rate_hz"]
    device = pick_device()
    print(f"device: {device}")

    step(1, "load the recordings")
    paths = list_recordings(resolve(cfg, "raw_dir"))
    recordings = {recording_stem(p): load_recording(p) for p in paths}  # load_recording also checks every file
    print(f"{len(recordings)} recordings, {sum(len(d) for d in recordings.values()):,} rows")

    step(2, "check the data")
    checks = run_checks(recordings, cfg["split"]["min_files_for_holdout"], cfg["data"]["max_gap_s"])
    print(checks.to_string(index=False))

    step(3, "clean and resample the data")
    segments, manifest = build_interim(cfg)
    print(f"{len(segments)} segments, {int(manifest.frames.sum())} frames at {rate} Hz")

    step(4, "build windows, features and targets")
    windows = build_windows(segments, cfg)
    n_windows = sum(len(w["x"]) for w in windows.values())
    print(f"{n_windows:,} windows, each with {cfg['window_frames']} frames x {N_FEATURES} features and {9 * len(horizons)} targets")

    step(5, "split by recording")
    files = sorted(windows)
    splits = make_splits(files, cfg)
    for s in splits:
        check_split(s)
    print(f"{len(files)} recordings -> {len(splits)} split(s); every recording is a test recording exactly once")
    test_files = pooled_test_files(splits)
    test = stack(windows, test_files)

    step(6, "score the baselines")
    baseline_offsets = {
        "stay still": predict_still(len(test["y"]), len(horizons)),
        "constant velocity": predict_constant_velocity(test["raw"], rate, horizons, cfg["data"]["velocity_mean_frames"]),
    }
    baseline_tables = {name: error_table(displacement_errors_cm(p, test["y"]), test["file_idx"], cfg) for name, p in baseline_offsets.items()}
    for name, table in baseline_tables.items():
        print(f"{name}: {overall(table):.1f} cm")
    baseline_report = pd.concat([t.assign(model=n) for n, t in baseline_tables.items()])
    baseline_report[["model", "horizon_s", "entity", "mean_cm", "ci_low", "ci_high"]].to_csv(reports_dir / "baselines.csv", index=False)

    step(7, "train the models (cross-validation)")
    folds_dir = models_dir / "cv_folds"
    histories = {}
    for kind in ("gru", "mlp"):
        print(f"{kind}: {n_params(build_model(kind, cfg)):,} parameters")
        start = time.perf_counter()
        histories[kind] = cross_validate(kind, cfg, windows, splits, folds_dir, device, progress=False)
        print(f"{kind}: {len(splits)} folds trained in {time.perf_counter() - start:.1f} s")
    summary = summarise_histories(histories, splits)
    summary.to_csv(reports_dir / "training_summary.csv", index=False)
    plot_training_curves({k.upper(): v for k, v in histories.items()}, figures_dir / "training_curves.png")

    step(8, "evaluate on the test recordings")
    predictions = {kind: pooled_predictions(kind, cfg, windows, splits, folds_dir) for kind in ("gru", "mlp")}
    true, raw, file_idx = predictions["gru"]["true"], predictions["gru"]["raw"], predictions["gru"]["file_idx"]
    offsets = {**baseline_offsets, "GRU": predictions["gru"]["pred"], "MLP": predictions["mlp"]["pred"]}
    tables = {name: error_table(displacement_errors_cm(p, true), file_idx, cfg) for name, p in offsets.items()}
    for name, table in tables.items():
        print(f"{name}: {overall(table):.1f} cm")
    results = pd.concat([t.assign(model=n) for n, t in tables.items()])
    results[["model", "horizon_s", "entity", "mean_cm", "ci_low", "ci_high"]].to_csv(reports_dir / "results_test.csv", index=False)
    plot_error_by_horizon(tables, cfg, figures_dir / "test_error_by_horizon.png")
    picks = pick_windows(len(true), 6, cfg["seed"])
    plot_trajectories(raw, true, {"GRU": offsets["GRU"], "constant velocity": offsets["constant velocity"]}, picks, cfg, figures_dir / "trajectories.png")

    step(9, "train the final model and export it")
    kind, epochs = pick_final_model(summary)
    print(f"chosen by validation loss: {kind.upper()}, trained again on all recordings for {epochs} epochs")
    model, norm, history = final_refit(kind, cfg, windows, files, epochs, device, progress=False)
    model = model.cpu().eval()
    save_final_files(model, norm, cfg, models_dir, kind, epochs, files)

    names = cfg["export"]
    onnx_path = export_onnx(model, cfg, models_dir / names["onnx_file"])
    print("ONNX shapes:", onnx_io_shapes(onnx_path))
    print("ONNX operators:", onnx_ops(onnx_path))
    all_windows = stack(windows, files)
    chosen = np.random.default_rng(cfg["seed"]).choice(len(all_windows["x"]), 20, replace=False)
    difference = parity(model, onnx_path, norm_x(all_windows["x"][chosen], norm))
    print(f"largest difference between PyTorch and ONNX on 20 windows: {difference:.2e}")
    if difference >= names["parity_tolerance"]:
        raise RuntimeError(f"the ONNX model differs from the PyTorch model by {difference:.2e}, more than the allowed {names['parity_tolerance']}")

    i = chosen[0]
    golden_vector(model, norm, all_windows["raw"][i], all_windows["x"][i], cfg, models_dir / names["golden_vector_file"], onnx_path)
    write_model_card(
        models_dir / names["model_card_file"],
        {
            "cfg": cfg,
            "results": results_summary(tables, [h / rate for h in horizons]),
            "verdict": verdict_text(kind.upper(), tables),
            "exported_kind": kind,
            "n_params": n_params(model),
            "opset": names["opset"],
            "n_files": len(files),
            "minutes": sum(s.frames for s in segments) / rate / 60,
            "n_windows": int(len(all_windows["x"])),
            "fold_sizes": [len(s.test) for s in splits],
            "final_epochs": epochs,
        },
    )
    print(f"\nDone. The final model and its files are in {models_dir}")
    print(f"Best baseline: {best_baseline(tables)} ({overall(tables[best_baseline(tables)]):.1f} cm). Chosen model: {kind.upper()} ({overall(tables[kind.upper()]):.1f} cm).")


if __name__ == "__main__":
    run()
