"""Step 5: split the data, normalise it, and wrap it for PyTorch.

The data is split by RECORDING (file), never by window. Neighbouring windows of one recording overlap heavily, so if they
ended up in different splits the model would be tested on data it has almost seen. The dataset has no participant IDs,
so the recording is the best unit available.

With fewer than ``split.min_files_for_holdout`` recordings, one hold-out split would leave a test set of a single file.
In that case the config asks for cross-validation instead: every recording is used as test data exactly once.
"""

from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import numpy as np
import pandas as pd
import torch
from torch.utils.data import DataLoader, Dataset

from .feature_engineering import segment_windows
from .preprocessing import Segment

# Windows


def build_windows(segments: list[Segment], cfg: dict[str, Any]) -> dict[str, dict[str, np.ndarray]]:
    """Make the windows of every segment and group them by recording: ``{recording: {x, y, raw, t0, segment}}``.

    ``segment`` holds the segment number of each window. Windows from different segments of one recording are stored
    together, but a single window never mixes two segments.
    """
    parts: dict[str, list[dict[str, np.ndarray]]] = {}
    for s in segments:
        w = segment_windows(s, cfg)
        w["segment"] = np.full(len(w["t0"]), s.index)
        parts.setdefault(s.source, []).append(w)
    keys = ("x", "y", "raw", "t0", "segment")
    return {name: {k: np.concatenate([w[k] for w in ws]) for k in keys} for name, ws in parts.items()}


def stack(windows: dict[str, dict[str, np.ndarray]], files: list[str]) -> dict[str, np.ndarray]:
    """Join the windows of the given recordings.

    The result also has ``file_idx``: for each window, the position of its recording in ``files``.
    """
    xs, ys, raws, idx = [], [], [], []
    for i, name in enumerate(files):
        w = windows[name]
        xs.append(w["x"])
        ys.append(w["y"])
        raws.append(w["raw"])
        idx.append(np.full(len(w["x"]), i))
    return {"x": np.concatenate(xs), "y": np.concatenate(ys), "raw": np.concatenate(raws), "file_idx": np.concatenate(idx)}


# Splits


@dataclass(frozen=True)
class Split:
    """Which recordings are used for training, validation and testing. ``name`` is 'holdout' or 'fold0', 'fold1', ..."""

    name: str
    train: tuple[str, ...]
    val: tuple[str, ...]  # used to decide when to stop training
    test: tuple[str, ...]  # used only for the final scores

    def files(self) -> list[str]:
        return [*self.train, *self.val, *self.test]


def _take_val(files: list[str], fraction: float, rng: np.random.Generator) -> tuple[list[str], list[str]]:
    """Set aside at least one recording (about ``fraction`` of them) for validation. Returns (train, val)."""
    order = [str(f) for f in rng.permutation(files)]
    n_val = max(1, int(round(len(order) * fraction)))
    return order[n_val:], order[:n_val]


def holdout_split(files: list[str], cfg: dict[str, Any]) -> Split:
    """One random train / validation / test split by recording, using the ratios in ``split``."""
    rng = np.random.default_rng(cfg["seed"])
    order = [str(f) for f in rng.permutation(sorted(files))]
    n_test = max(1, int(round(len(order) * cfg["split"]["test"])))
    n_val = max(1, int(round(len(order) * cfg["split"]["val"])))
    return Split("holdout", tuple(order[n_test + n_val :]), tuple(order[n_test : n_test + n_val]), tuple(order[:n_test]))


def kfold_splits(files: list[str], cfg: dict[str, Any]) -> list[Split]:
    """Cross-validation by recording.

    The recordings are shuffled and cut into k groups. In fold i, group i is the test set. About 15% of the remaining
    recordings (at least one) are used for validation and the rest for training. Every recording is tested exactly once.
    """
    k = min(cfg["split"]["cv_folds"], len(files))
    rng = np.random.default_rng(cfg["seed"])
    order = [str(f) for f in rng.permutation(sorted(files))]
    groups = [[str(f) for f in g] for g in np.array_split(order, k)]
    splits = []
    for i, test in enumerate(groups):
        others = [f for j, g in enumerate(groups) if j != i for f in g]
        train, val = _take_val(others, cfg["train"]["val_fraction_of_train_files"], np.random.default_rng(cfg["seed"] + i))
        splits.append(Split(f"fold{i}", tuple(sorted(train)), tuple(sorted(val)), tuple(sorted(test))))
    return splits


def make_splits(files: list[str], cfg: dict[str, Any]) -> list[Split]:
    """One hold-out split if there are enough recordings, otherwise one split per cross-validation fold."""
    if len(files) >= cfg["split"]["min_files_for_holdout"]:
        return [holdout_split(files, cfg)]
    return kfold_splits(files, cfg)


def check_split(split: Split) -> None:
    """Raise an error if a recording appears in more than one of train, validation and test."""
    names = split.files()
    if len(set(names)) != len(names):
        raise ValueError(f"{split.name}: a recording is in more than one of train / val / test")


def pooled_test_files(splits: list[Split]) -> list[str]:
    """All test recordings of all splits, in order. With cross-validation each recording appears exactly once."""
    files = [f for s in splits for f in s.test]
    if len(set(files)) != len(files):
        raise ValueError("a recording is a test file in more than one split")
    return files


def split_summary(split: Split, windows: dict[str, dict[str, np.ndarray]], segments: list[Segment], cfg: dict[str, Any]) -> pd.DataFrame:
    """Table with the number of recordings, segments, windows and minutes in each part of a split."""
    rows = []
    for part in ("train", "val", "test"):
        files = getattr(split, part)
        segs = [s for s in segments if s.source in files]
        rows.append(
            {
                "split": split.name,
                "part": part,
                "files": len(files),
                "segments": len(segs),
                "windows": int(sum(len(windows[f]["x"]) for f in files)),
                "minutes": round(sum(s.frames for s in segs) / cfg["sample_rate_hz"] / 60, 2),
            }
        )
    return pd.DataFrame(rows)


# Normalisation


@dataclass(frozen=True)
class Norm:
    """Mean and standard deviation of every feature (``x_``) and every target (``y_``), taken from the training data only."""

    x_mean: np.ndarray
    x_std: np.ndarray
    y_mean: np.ndarray
    y_std: np.ndarray


def _safe_std(std: np.ndarray) -> np.ndarray:
    """Replace a standard deviation of 0 by 1. A column that never changes (like grip) must not cause a division by zero."""
    return np.where(std < 1e-8, 1.0, std)


def compute_norm(x_train: np.ndarray, y_train: np.ndarray) -> Norm:
    """Mean and standard deviation of the training windows. Never use validation or test data here."""
    flat = x_train.reshape(-1, x_train.shape[-1]).astype(np.float64)  # all frames of all windows, one row each
    y = y_train.astype(np.float64)
    return Norm(flat.mean(0), _safe_std(flat.std(0)), y.mean(0), _safe_std(y.std(0)))


def norm_x(x: np.ndarray, n: Norm) -> np.ndarray:
    """Scale features to mean 0 and standard deviation 1."""
    return ((x - n.x_mean) / n.x_std).astype(np.float32)


def norm_y(y: np.ndarray, n: Norm) -> np.ndarray:
    """Scale targets to mean 0 and standard deviation 1."""
    return ((y - n.y_mean) / n.y_std).astype(np.float32)


def denorm_y(y: np.ndarray, n: Norm) -> np.ndarray:
    """Turn the model's scaled output back into metres."""
    return (np.asarray(y) * n.y_std + n.y_mean).astype(np.float32)


def save_norm(n: Norm, path: str | Path, meta: dict[str, Any] | None = None) -> None:
    """Save the normalisation numbers as a JSON file, together with optional extra information (``meta``)."""
    data = {"x_mean": n.x_mean.tolist(), "x_std": n.x_std.tolist(), "y_mean": n.y_mean.tolist(), "y_std": n.y_std.tolist(), **(meta or {})}
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    Path(path).write_text(json.dumps(data, indent=1), encoding="utf-8")


def load_norm(path: str | Path) -> Norm:
    """Read a file written by :func:`save_norm`."""
    data = json.loads(Path(path).read_text(encoding="utf-8"))
    return Norm(*(np.asarray(data[k], dtype=np.float64) for k in ("x_mean", "x_std", "y_mean", "y_std")))


# PyTorch


class WindowDataset(Dataset):
    """Normalised windows: features of shape (T, 22) and targets of shape (36,)."""

    def __init__(self, x: np.ndarray, y: np.ndarray, n: Norm):
        self.x = torch.from_numpy(norm_x(x, n))
        self.y = torch.from_numpy(norm_y(y, n))

    def __len__(self) -> int:
        return len(self.x)

    def __getitem__(self, i: int) -> tuple[torch.Tensor, torch.Tensor]:
        return self.x[i], self.y[i]


def make_loader(x: np.ndarray, y: np.ndarray, n: Norm, batch_size: int, shuffle: bool, seed: int = 0) -> DataLoader:
    """A PyTorch data loader over normalised windows. The shuffling is seeded, so runs can be repeated."""
    generator = torch.Generator().manual_seed(seed)
    return DataLoader(WindowDataset(x, y, n), batch_size=batch_size, shuffle=shuffle, generator=generator if shuffle else None)
