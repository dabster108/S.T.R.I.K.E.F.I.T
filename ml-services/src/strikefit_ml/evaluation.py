"""Step 6 (baselines) and step 8 (scoring): how good are the predictions?

Score: the mean displacement error in centimetres. For one tracked point at one horizon it is the straight-line distance
between the predicted and the true movement, averaged over all test windows.

Confidence intervals come from a bootstrap that resamples whole RECORDINGS with replacement. Windows of one recording are
strongly related, so resampling single windows would make the intervals far too narrow.
"""

from __future__ import annotations

from typing import Any

import numpy as np
import pandas as pd

ENTITY_LABELS = ("head", "controlling hand", "secondary hand")
BASELINE_NAMES = ("stay still", "constant velocity")


# Baselines: simple guesses that need no training. A trained model is only useful if it beats them.


def predict_still(n_windows: int, n_horizons: int) -> np.ndarray:
    """Baseline "stay still": predict no movement at all (all zeros). Shape (n, 9 * horizons)."""
    return np.zeros((n_windows, 9 * n_horizons), dtype=np.float32)


def mean_velocity(raw: np.ndarray, rate_hz: float, frames: int) -> np.ndarray:
    """Average velocity (m/s) of each tracked point over the last ``frames`` steps of each window.

    ``raw`` has shape (n, T, 10), in metres. Returns shape (n, 3 points, 3 axes).
    """
    pos = raw[:, :, :9].reshape(len(raw), raw.shape[1], 3, 3)
    return (pos[:, -1] - pos[:, -1 - frames]) / frames * rate_hz


def predict_constant_velocity(raw: np.ndarray, rate_hz: float, horizons_frames: list[int], velocity_frames: int) -> np.ndarray:
    """Baseline "constant velocity": every point keeps moving with its recent average velocity.

    The result has the same layout as the targets: first by horizon, then by tracked point, then by axis.
    """
    v = mean_velocity(raw, rate_hz, velocity_frames)  # shape (n, 3, 3)
    per_horizon = [v * (h / rate_hz) for h in horizons_frames]
    return np.stack(per_horizon, axis=1).reshape(len(raw), -1).astype(np.float32)


# The score


def displacement_errors_cm(pred_m: np.ndarray, true_m: np.ndarray) -> np.ndarray:
    """Error in cm for every window, horizon and tracked point. Inputs have shape (n, 36) in metres, output (n, horizons, 3)."""
    diff = (pred_m - true_m).reshape(len(true_m), -1, 3, 3)  # (windows, horizons, points, axes)
    return np.linalg.norm(diff, axis=-1) * 100.0


def _cells(n_h: int) -> dict[tuple[str, str], tuple[list[int], list[int]]]:
    """Rows of the result table. Each row averages over some horizons and some tracked points."""
    out: dict[tuple[str, str], tuple[list[int], list[int]]] = {}
    for h in range(n_h):
        for e in range(3):
            out[(str(h), ENTITY_LABELS[e])] = ([h], [e])
        out[(str(h), "all entities")] = ([h], [0, 1, 2])
    for e in range(3):
        out[("all horizons", ENTITY_LABELS[e])] = (list(range(n_h)), [e])
    out[("all horizons", "all entities")] = (list(range(n_h)), [0, 1, 2])
    return out


def error_table(err: np.ndarray, file_idx: np.ndarray, cfg: dict[str, Any], seed: int | None = None) -> pd.DataFrame:
    """Mean error (cm) with a confidence interval for each horizon and tracked point, plus the summary rows.

    ``err`` comes from :func:`displacement_errors_cm`. ``file_idx`` says which recording each window belongs to.
    """
    n_files = int(file_idx.max()) + 1
    n_h = err.shape[1]
    sums = np.zeros((n_files, n_h, 3))
    np.add.at(sums, file_idx, err)  # total error of each recording
    counts = np.bincount(file_idx, minlength=n_files).astype(float)

    rng = np.random.default_rng(cfg["seed"] if seed is None else seed)
    draws = rng.integers(0, n_files, size=(cfg["evaluation"]["bootstrap_resamples"], n_files))  # which recordings each resample picks
    low_q = (1 - cfg["evaluation"]["ci_level"]) / 2 * 100
    high_q = (1 + cfg["evaluation"]["ci_level"]) / 2 * 100
    horizon_s = [h / cfg["sample_rate_hz"] for h in cfg["horizons_frames"]]

    rows = []
    for (horizon_key, entity_key), (horizon_ids, entity_ids) in _cells(n_h).items():
        cell = sums[:, horizon_ids][:, :, entity_ids].sum(axis=(1, 2))  # total error of each recording in this row
        per_window = len(horizon_ids) * len(entity_ids)
        mean = cell.sum() / (counts.sum() * per_window)
        resampled = cell[draws].sum(axis=1) / (counts[draws].sum(axis=1) * per_window)
        rows.append(
            {
                "horizon_s": "all" if horizon_key == "all horizons" else horizon_s[int(horizon_key)],
                "entity": entity_key,
                "mean_cm": float(mean),
                "ci_low": float(np.percentile(resampled, low_q)),
                "ci_high": float(np.percentile(resampled, high_q)),
            }
        )
    return pd.DataFrame(rows)


def compare_tables(tables: dict[str, pd.DataFrame]) -> pd.DataFrame:
    """One row per (horizon, tracked point) and one column per model, each cell written as "mean [low, high]"."""
    first = next(iter(tables.values()))
    out = first[["horizon_s", "entity"]].copy()
    for name, t in tables.items():
        out[name] = [f"{m:.2f} [{lo:.2f}, {hi:.2f}]" for m, lo, hi in zip(t.mean_cm, t.ci_low, t.ci_high)]
    return out


def overall_row(table: pd.DataFrame) -> pd.Series:
    """The summary row that averages over every horizon and every tracked point."""
    return table[(table.horizon_s == "all") & (table.entity == "all entities")].iloc[0]


def overall(table: pd.DataFrame) -> float:
    """The headline number: mean error in cm over every horizon and every tracked point."""
    return float(overall_row(table).mean_cm)


def per_horizon(table: pd.DataFrame, horizons_s: list[float]) -> list[float]:
    """Mean error in cm (all tracked points) for each horizon."""
    return [float(table[(table.horizon_s == h) & (table.entity == "all entities")].mean_cm.iloc[0]) for h in horizons_s]


def intervals_overlap(a: pd.DataFrame, b: pd.DataFrame) -> bool:
    """True if the overall confidence intervals of two models overlap, i.e. we cannot be sure one is better."""
    ra, rb = overall_row(a), overall_row(b)
    return not (ra.ci_high < rb.ci_low or rb.ci_high < ra.ci_low)


def best_baseline(tables: dict[str, pd.DataFrame]) -> str:
    """Name of the baseline with the lowest overall error."""
    return min(BASELINE_NAMES, key=lambda name: overall(tables[name]))


def results_summary(tables: dict[str, pd.DataFrame], horizons_s: list[float]) -> dict[str, dict[str, Any]]:
    """Per model: overall error with its interval and the error for each horizon. Used for the model card."""
    out = {}
    for name, table in tables.items():
        row = overall_row(table)
        out[name] = {"overall": float(row.mean_cm), "low": float(row.ci_low), "high": float(row.ci_high), "per_horizon": per_horizon(table, horizons_s)}
    return out


def verdict_text(model_name: str, tables: dict[str, pd.DataFrame]) -> str:
    """One short paragraph that says whether the model beats the best baseline. ``model_name`` is 'GRU' or 'MLP'."""
    baseline = best_baseline(tables)
    model_cm, baseline_cm = overall(tables[model_name]), overall(tables[baseline])
    text = f"**Does it beat the baselines?** The {model_name} has an overall error of {model_cm:.1f} cm. The best baseline ({baseline}) has {baseline_cm:.1f} cm. "
    if model_cm >= baseline_cm:
        return text + "The model is NOT better than the best baseline."
    if intervals_overlap(tables[model_name], tables[baseline]):
        return text + "The model is better, but the 95% intervals overlap, so with this little data the result is not firm."
    return text + "The model is better, and the 95% intervals do not overlap."


# Plot


def pick_windows(n: int, count: int, seed: int) -> np.ndarray:
    """Choose ``count`` random window numbers out of ``n`` (sorted, seeded) for the trajectory figure."""
    return np.sort(np.random.default_rng(seed).choice(n, size=min(count, n), replace=False))


def plot_trajectories(raw: np.ndarray, true: np.ndarray, pred: dict[str, np.ndarray], picks: np.ndarray, cfg: dict[str, Any], path: str) -> None:
    """Draw predicted against true future positions for the chosen windows.

    Each window gets two plots: a top-down view (x, y) and the height over time. Lines show the 1.0 s of past movement,
    filled markers the true future positions, open markers the predictions of each model (``pred``: model name -> offsets in metres).
    Colours: head blue, controlling hand red, secondary hand green.
    """
    import matplotlib

    matplotlib.use("Agg", force=False)
    import matplotlib.pyplot as plt

    rate, horizons = cfg["sample_rate_hz"], cfg["horizons_frames"]
    colours = ("tab:blue", "tab:red", "tab:green")
    markers = ("s", "^", "D", "v")
    fig, axes = plt.subplots(len(picks), 2, figsize=(11, 3.1 * len(picks)))
    t_past = (np.arange(raw.shape[1]) - (raw.shape[1] - 1)) / rate
    for row, i in enumerate(picks):
        pos = raw[i, :, :9].reshape(-1, 3, 3)  # (frames, tracked point, axis)
        now = pos[-1]
        ax_xy, ax_z = axes[row]
        for e in range(3):
            ax_xy.plot(pos[:, e, 0], pos[:, e, 1], color=colours[e], lw=1.3)
            ax_z.plot(t_past, pos[:, e, 2], color=colours[e], lw=1.3, label=ENTITY_LABELS[e] if row == 0 else None)
            truth = true[i].reshape(len(horizons), 3, 3)[:, e] + now[e]
            ax_xy.scatter(truth[:, 0], truth[:, 1], color=colours[e], marker="o", s=22, zorder=3)
            ax_z.scatter(np.array(horizons) / rate, truth[:, 2], color=colours[e], marker="o", s=22, zorder=3)
            for k, (name, p) in enumerate(pred.items()):
                guess = p[i].reshape(len(horizons), 3, 3)[:, e] + now[e]
                label = name if (row == 0 and e == 0) else None
                ax_xy.scatter(guess[:, 0], guess[:, 1], facecolors="none", edgecolors=colours[e], marker=markers[k], s=40, zorder=4, label=label)
                ax_z.scatter(np.array(horizons) / rate, guess[:, 2], facecolors="none", edgecolors=colours[e], marker=markers[k], s=40, zorder=4)
        ax_xy.set_aspect("equal", adjustable="datalim")
        ax_xy.set_xlabel("x (m)")
        ax_xy.set_ylabel("y (m)")
        ax_xy.set_title(f"window {i}: top-down view", fontsize=9)
        ax_xy.grid(alpha=0.3)
        ax_z.set_xlabel("time from now (s)")
        ax_z.set_ylabel("height z (m)")
        ax_z.set_title("height over time", fontsize=9)
        ax_z.grid(alpha=0.3)
    axes[0][0].legend(fontsize=7, title="open markers = model", title_fontsize=7)
    axes[0][1].legend(fontsize=7, title="filled markers = truth", title_fontsize=7)
    fig.tight_layout()
    fig.savefig(path, dpi=120)
    plt.close(fig)


def plot_error_by_horizon(tables: dict[str, pd.DataFrame], cfg: dict[str, Any], path: str) -> None:
    """Plot the mean error against the prediction horizon, one panel per tracked point, one line per model (with intervals)."""
    import matplotlib

    matplotlib.use("Agg", force=False)
    import matplotlib.pyplot as plt

    horizons_s = [h / cfg["sample_rate_hz"] for h in cfg["horizons_frames"]]
    colours = ("tab:gray", "tab:orange", "tab:blue", "tab:green")
    fig, axes = plt.subplots(1, 3, figsize=(14, 3.8))
    for ax, entity in zip(axes, ENTITY_LABELS):
        for (name, table), colour in zip(tables.items(), colours):
            rows = table[(table.entity == entity) & (table.horizon_s != "all")]
            yerr = [rows.mean_cm - rows.ci_low, rows.ci_high - rows.mean_cm]
            ax.errorbar(horizons_s, rows.mean_cm, yerr=yerr, marker="o", ms=3, capsize=2, color=colour, label=name)
        ax.set_title(entity)
        ax.set_xlabel("how far ahead (s)")
        ax.grid(alpha=0.3)
    axes[0].set_ylabel("mean displacement error (cm)")
    axes[0].legend(fontsize=7)
    fig.tight_layout()
    fig.savefig(path, dpi=130)
    plt.close(fig)
