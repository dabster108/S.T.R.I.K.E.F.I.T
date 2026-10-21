"""Step 4: turn the cleaned segments into model inputs (features) and answers (targets).

A window is ``window_frames`` frames in a row. For each frame it holds the head position, the controlling-hand position,
the secondary-hand position (all in metres) and the grip flag. The last frame of the window is "now" (called t0).

Features: 22 numbers per frame
    - 9: position of the head and both hands relative to where the head is now (x, y, z each)
    - 9: velocity of the head and both hands (m/s)
    - 2: speed of the controlling hand and of the secondary hand
    - 1: height of the head
    - 1: grip

Targets: 36 numbers. For each horizon h (0.25, 0.5, 0.75 and 1.0 s), the movement of the head, the controlling hand and the
secondary hand between t0 and t0 + h (x, y, z each). That is 4 horizons x 3 points x 3 axes.

Rotations are not used. A window never goes outside its segment.
"""

from __future__ import annotations

from typing import Any

import numpy as np

from .ingestion import AXES, ENTITIES
from .preprocessing import Segment

N_FEATURES = 22
ENTITY_KEYS = ("head", "controlling_hand", "secondary_hand")  # same order as ENTITIES in ingestion.py

FEATURE_NAMES: list[str] = (
    [f"head_rel_{a}" for a in AXES]
    + [f"controlling_hand_rel_{a}" for a in AXES]
    + [f"secondary_hand_rel_{a}" for a in AXES]
    + [f"head_vel_{a}" for a in AXES]
    + [f"controlling_hand_vel_{a}" for a in AXES]
    + [f"secondary_hand_vel_{a}" for a in AXES]
    + ["controlling_hand_speed", "secondary_hand_speed", "head_height_z", "grip"]
)
assert len(FEATURE_NAMES) == N_FEATURES

# Columns of the raw window array (metres; grip is 0 or 1).
WINDOW_COLUMNS: list[str] = [f"{e}_{a}" for e in ENTITIES for a in AXES] + ["Grip"]


def target_names(horizons_frames: list[int]) -> list[str]:
    """Names of the targets: first by horizon, then by tracked point (head, controlling hand, secondary hand), then by axis."""
    return [f"h{h}_{e}_d{a}" for h in horizons_frames for e in ENTITY_KEYS for a in AXES]


def segment_array(seg: Segment) -> np.ndarray:
    """The segment as an array of shape (frames, 10): head xyz, controlling-hand xyz, secondary-hand xyz, grip."""
    return seg.data[WINDOW_COLUMNS].to_numpy(dtype=np.float64)


def velocities(pos: np.ndarray, rate_hz: float) -> np.ndarray:
    """Velocity in m/s from positions of shape (..., T, 3), using the difference between neighbouring frames.

    The first frame has no earlier frame, so it simply repeats the velocity of the second frame.
    """
    v = np.diff(pos, axis=-2) * rate_hz
    return np.concatenate([v[..., :1, :], v], axis=-2)


def window_features(windows: np.ndarray, rate_hz: float) -> np.ndarray:
    """Features of shape (n, T, 22) from raw windows of shape (n, T, 10)."""
    w = np.asarray(windows, dtype=np.float64)
    head, ch, sh = w[..., 0:3], w[..., 3:6], w[..., 6:9]
    head_now = head[:, -1:, :]  # head position in the last frame of each window
    v_head, v_ch, v_sh = (velocities(p, rate_hz) for p in (head, ch, sh))
    feats = np.concatenate(
        [
            head - head_now, ch - head_now, sh - head_now,
            v_head, v_ch, v_sh,
            np.linalg.norm(v_ch, axis=-1, keepdims=True),
            np.linalg.norm(v_sh, axis=-1, keepdims=True),
            head[..., 2:3],
            w[..., 9:10],
        ],
        axis=-1,
    )
    return feats.astype(np.float32)


def window_targets(positions: np.ndarray, t0: np.ndarray, horizons_frames: list[int]) -> np.ndarray:
    """Targets of shape (n, 9 * number of horizons) for the "now" frames ``t0`` of a segment.

    ``positions`` has shape (frames, at least 9). Each target is the position at t0 + h minus the position at t0.
    """
    cols = []
    for h in horizons_frames:
        for e in range(3):
            sl = slice(3 * e, 3 * e + 3)
            cols.append(positions[t0 + h, sl] - positions[t0, sl])
    return np.concatenate(cols, axis=1).astype(np.float32)


def make_windows(arr: np.ndarray, window_frames: int, horizons_frames: list[int], stride_frames: int, rate_hz: float) -> dict[str, np.ndarray]:
    """All windows of ONE segment. ``arr`` has shape (frames, 10).

    A window needs enough frames before t0 for the input and enough frames after t0 for the furthest target.
    Returns ``x`` (features), ``y`` (targets), ``t0`` (frame number of "now" in each window) and ``raw`` (the unprocessed windows).
    """
    last_t0 = len(arr) - 1 - max(horizons_frames)
    t0 = np.arange(window_frames - 1, last_t0 + 1, stride_frames)
    if len(t0) == 0:  # the segment is too short for even one window
        return {
            "x": np.zeros((0, window_frames, N_FEATURES), np.float32),
            "y": np.zeros((0, 9 * len(horizons_frames)), np.float32),
            "t0": t0,
            "raw": np.zeros((0, window_frames, arr.shape[1])),
        }
    frame_numbers = t0[:, None] + np.arange(-(window_frames - 1), 1)[None, :]
    raw = arr[frame_numbers]  # shape (n, T, 10)
    return {"x": window_features(raw, rate_hz), "y": window_targets(arr, t0, horizons_frames), "t0": t0, "raw": raw}


def segment_windows(seg: Segment, cfg: dict[str, Any]) -> dict[str, np.ndarray]:
    """All windows of a segment, using the settings from the config file."""
    return make_windows(segment_array(seg), cfg["window_frames"], cfg["horizons_frames"], cfg["stride_frames"], cfg["sample_rate_hz"])
