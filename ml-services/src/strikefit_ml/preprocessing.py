"""Step 3: clean the recordings. Resample them to a fixed rate, convert to metres and cut them at time gaps.

The raw time steps are uneven (about 11 ms, with some jumps). Every recording is turned into one or more "segments".
A segment has a new frame exactly every 1/rate seconds, starting at time 0.
Nothing is ever filled in across a jump larger than ``data.max_gap_s``. The recording is cut there instead,
and each piece becomes its own segment.
"""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Any

import numpy as np
import pandas as pd
from scipy.spatial.transform import Rotation, Slerp

from .ingestion import (
    AXES,
    ENTITIES,
    POSITION_COLUMNS,
    QUAT,
    ROTATION_COLUMNS,
    list_recordings,
    load_recording,
    recording_stem,
    resolve,
)

# Columns of a resampled segment. The Gesture label is not needed, so it is dropped.
SEGMENT_COLUMNS = ["Time", *POSITION_COLUMNS, *ROTATION_COLUMNS, "Grip"]


@dataclass(frozen=True)
class Segment:
    """One continuous, resampled piece of a recording."""

    source: str  # name of the recording it came from
    index: int  # number of the segment inside that recording
    data: pd.DataFrame  # the SEGMENT_COLUMNS, positions in metres, Time starting at 0 in equal steps

    @property
    def name(self) -> str:
        return f"{self.source}__seg{self.index:02d}"

    @property
    def frames(self) -> int:
        return len(self.data)


def split_at_gaps(time: np.ndarray, max_gap_s: float) -> list[tuple[int, int]]:
    """Index ranges (start, stop) of the pieces of ``time`` that are separated by a step larger than ``max_gap_s``."""
    cuts = np.flatnonzero(np.diff(time) > max_gap_s) + 1
    bounds = [0, *cuts.tolist(), len(time)]
    return list(zip(bounds[:-1], bounds[1:]))


def resample_piece(piece: pd.DataFrame, rate_hz: float) -> pd.DataFrame:
    """Resample one continuous piece to ``rate_hz``.

    Positions are interpolated linearly, rotations with SLERP (the standard way to interpolate quaternions),
    and Grip takes the nearest value. Time starts at 0.
    """
    t = piece["Time"].to_numpy(float)
    t = t - t[0]
    step = 1.0 / rate_hz
    n = int(np.floor(t[-1] / step + 1e-9)) + 1  # number of new frames that fit inside the piece
    grid = np.arange(n) * step
    out: dict[str, np.ndarray] = {"Time": grid}
    for col in POSITION_COLUMNS:
        out[col] = np.interp(grid, t, piece[col].to_numpy(float))
    for e in ENTITIES:
        cols = [f"{e}_{c}" for c in QUAT]
        q = piece[cols].to_numpy(float)
        q = q / np.linalg.norm(q, axis=1, keepdims=True)
        slerp = Slerp(t, Rotation.from_quat(q[:, [1, 2, 3, 0]]))  # scipy wants x, y, z, w
        xyzw = slerp(grid).as_quat()
        wxyz = xyzw[:, [3, 0, 1, 2]]
        wxyz = np.where(wxyz[:, :1] < 0, -wxyz, wxyz)  # keep w >= 0, because q and -q describe the same rotation
        for i, c in enumerate(cols):
            out[c] = wxyz[:, i]
    grip = piece["Grip"].to_numpy(float)
    out["Grip"] = grip[np.abs(grid[:, None] - t[None, :]).argmin(axis=1)]  # value of the nearest original sample
    return pd.DataFrame(out)[SEGMENT_COLUMNS]


def resample_recording(df: pd.DataFrame, rate_hz: float, max_gap_s: float = 0.1, min_frames: int = 2) -> list[pd.DataFrame]:
    """Cut a checked recording at its time gaps and resample every piece (positions are still in the raw unit).

    A piece is dropped if it has fewer than ``min_frames`` raw rows or ends up with fewer than ``min_frames`` resampled frames.
    """
    pieces = []
    for a, b in split_at_gaps(df["Time"].to_numpy(float), max_gap_s):
        if b - a < min_frames:
            continue
        seg = resample_piece(df.iloc[a:b], rate_hz)
        if len(seg) >= min_frames:
            pieces.append(seg)
    return pieces


def to_metres(seg: pd.DataFrame, scale: float) -> pd.DataFrame:
    """A copy of the segment with all positions multiplied by ``scale`` (0.01 turns centimetres into metres)."""
    out = seg.copy()
    out[POSITION_COLUMNS] = out[POSITION_COLUMNS] * scale
    return out


def preprocess_file(path: str | Path, cfg: dict[str, Any]) -> list[Segment]:
    """Load one recording file, resample it and convert it to metres. Returns its gap-free segments."""
    df = load_recording(path)
    d = cfg["data"]
    pieces = resample_recording(df, cfg["sample_rate_hz"], d["max_gap_s"], d["min_segment_frames"])
    return [Segment(recording_stem(path), i, to_metres(p, cfg["units_scale_to_m"])) for i, p in enumerate(pieces)]


def save_segments(segments: list[Segment], out_dir: str | Path) -> pd.DataFrame:
    """Save every segment as ``<name>.npz`` plus a ``manifest.csv`` that lists them. Returns the manifest."""
    out = Path(out_dir)
    out.mkdir(parents=True, exist_ok=True)
    rows = []
    for s in segments:
        np.savez(out / f"{s.name}.npz", data=s.data.to_numpy(np.float32), columns=np.array(SEGMENT_COLUMNS))
        rows.append({"segment": s.name, "source_file": s.source, "segment_index": s.index, "frames": s.frames, "duration_s": float(s.data["Time"].iloc[-1])})
    manifest = pd.DataFrame(rows)
    manifest.to_csv(out / "manifest.csv", index=False)
    return manifest


def load_segments(interim_dir: str | Path) -> list[Segment]:
    """Read back the segments written by :func:`save_segments`, in the order of the manifest."""
    d = Path(interim_dir)
    manifest = pd.read_csv(d / "manifest.csv")
    segs = []
    for r in manifest.itertuples():
        z = np.load(d / f"{r.segment}.npz")
        segs.append(Segment(r.source_file, int(r.segment_index), pd.DataFrame(z["data"].astype(np.float64), columns=list(z["columns"]))))
    return segs


def build_interim(cfg: dict[str, Any]) -> tuple[list[Segment], pd.DataFrame]:
    """Clean every raw recording and save the result in the interim folder (all segments and the manifest)."""
    segments: list[Segment] = []
    for f in list_recordings(resolve(cfg, "raw_dir")):
        segments += preprocess_file(f, cfg)
    return segments, save_segments(segments, resolve(cfg, "interim_dir"))
