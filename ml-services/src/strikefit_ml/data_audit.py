"""Step 2: describe the raw data before using it.

These helpers only read the data. They never change it. They are used by notebooks/01_data_audit.ipynb.
"""

from __future__ import annotations

from pathlib import Path
from typing import Any

import numpy as np
import pandas as pd
from scipy.spatial.transform import Rotation

from .ingestion import AXES, ENTITIES, POSITION_COLUMNS, QUAT, recording_stem


def file_summary(path: str | Path, df: pd.DataFrame, gap_s: float) -> dict[str, Any]:
    """One row of the overview table: size, length and time-step statistics of a recording."""
    dt = np.diff(df["Time"].to_numpy(dtype=float))
    return {
        "file": recording_stem(path),
        "rows": len(df),
        "duration_s": float(df["Time"].iloc[-1] - df["Time"].iloc[0]),
        "first_time_s": float(df["Time"].iloc[0]),
        "dt_median_ms": float(np.median(dt) * 1e3),
        "dt_p95_ms": float(np.percentile(dt, 95) * 1e3),
        "dt_max_ms": float(dt.max() * 1e3),
        "gaps_over_limit": int((dt > gap_s).sum()),
        "longest_gap_s": float(dt.max()),
    }


def axis_summary(df: pd.DataFrame) -> pd.DataFrame:
    """Smallest, largest and average value of every position column (in the raw unit)."""
    return df[POSITION_COLUMNS].agg(["min", "max", "mean"]).T


def quaternion_norm_deviation(df: pd.DataFrame) -> dict[str, float]:
    """For each tracked point, the largest distance of a quaternion's length from 1. Zero means perfectly normalised."""
    out = {}
    for e in ENTITIES:
        q = df[[f"{e}_{c}" for c in QUAT]].to_numpy(dtype=float)
        out[e] = float(np.abs(np.linalg.norm(q, axis=1) - 1.0).max())
    return out


def frame_evidence(dfs: list[pd.DataFrame]) -> pd.DataFrame:
    """One row per recording with hints about the world axes (which way is up and which way is forward).

    These numbers are hints, not proof. Columns:
    - head_tilt_deg: average angle between the head's own z axis and the world z axis. A small value means z is up.
    - head_yaw_mean_deg, head_yaw_std_deg: the direction the head faces. A mean near 0 with a small spread means
      the players all face about the same way.
    - ch_offset_*_cm, sh_offset_*_cm: average sideways position of the controlling / secondary hand relative to the head.
    - ch_reach_dir_deg, sh_reach_dir_deg: direction (degrees from +x towards +y) in which each hand is found when it is
      farthest from the head. This shows where the strikes reach.
    - head_motion_axis_deg: direction of the head's main sideways movement.
    - head_motion_axis_share: how much of that movement is along the main direction (1 = a straight line).
    """
    rows = []
    for df in dfs:
        q = df[[f"Head_{c}" for c in QUAT]].to_numpy(float)
        q = q / np.linalg.norm(q, axis=1, keepdims=True)
        matrices = Rotation.from_quat(q[:, [1, 2, 3, 0]]).as_matrix()  # scipy wants x, y, z, w
        tilt = np.degrees(np.arccos(np.clip(matrices[:, 2, 2], -1, 1)))
        yaw = np.degrees(np.arctan2(matrices[:, 1, 0], matrices[:, 0, 0]))

        head = df[[f"Head_{a}" for a in AXES]].to_numpy(float)
        ch = df[[f"ControllingHand_{a}" for a in AXES]].to_numpy(float) - head
        sh = df[[f"SecondaryHand_{a}" for a in AXES]].to_numpy(float) - head

        reach = {}
        for key, rel in (("ch", ch), ("sh", sh)):
            distance = np.linalg.norm(rel[:, :2], axis=1)
            far = distance > np.percentile(distance, 90)  # the farthest 10% of the time
            unit = rel[far, :2] / distance[far, None]
            reach[key] = float(np.degrees(np.arctan2(unit[:, 1].mean(), unit[:, 0].mean())))

        xy = head[:, :2] - head[:, :2].mean(0)
        variance, directions = np.linalg.eigh(np.cov(xy.T))
        main = directions[:, np.argmax(variance)]

        rows.append(
            {
                "head_tilt_deg": float(tilt.mean()),
                "head_yaw_mean_deg": float(yaw.mean()),
                "head_yaw_std_deg": float(yaw.std()),
                "ch_offset_x_cm": float(ch[:, 0].mean()),
                "ch_offset_y_cm": float(ch[:, 1].mean()),
                "sh_offset_x_cm": float(sh[:, 0].mean()),
                "sh_offset_y_cm": float(sh[:, 1].mean()),
                "ch_reach_dir_deg": reach["ch"],
                "sh_reach_dir_deg": reach["sh"],
                "head_motion_axis_deg": float(np.degrees(np.arctan2(main[1], main[0])) % 180),
                "head_motion_axis_share": float(variance.max() / variance.sum()),
            }
        )
    return pd.DataFrame(rows)


def run_checks(dfs: dict[str, pd.DataFrame], n_files_needed_for_holdout: int, gap_s: float) -> pd.DataFrame:
    """Check the facts the rest of the pipeline relies on. Returns a table with the columns check, holds and evidence."""
    pooled = pd.concat(dfs.values(), ignore_index=True)
    head_z = pooled["Head_z"]
    steps = np.concatenate([np.diff(d["Time"].to_numpy(float)) for d in dfs.values()])
    quaternion_error = max(max(quaternion_norm_deviation(d).values()) for d in dfs.values())
    gap_rows = [int(i) for d in dfs.values() for i in np.flatnonzero(np.diff(d["Time"].to_numpy(float)) > gap_s)]
    rows = [
        ("positions are in centimetres", 100 < head_z.median() < 220, f"median head height is {head_z.median():.0f}"),
        ("z is the up axis", head_z.std() < 15, f"head height varies by only {head_z.std():.0f} (standard deviation)"),
        ("quaternions have length 1", quaternion_error < 1e-5, f"largest deviation {quaternion_error:.1e}"),
        ("the time steps are regular", 5 <= np.median(steps) * 1e3 <= 20, f"median step {np.median(steps) * 1e3:.1f} ms, about {1 / np.median(steps):.0f} Hz"),
        (f"time jumps over {gap_s * 1e3:.0f} ms only happen after the first row", all(r == 0 for r in gap_rows), f"{len(gap_rows)} jumps found, at rows {sorted(set(gap_rows))}"),
        ("grip never changes", pooled["Grip"].nunique() == 1, f"values found: {sorted(pooled['Grip'].unique().tolist())}"),
        (f"there are at least {n_files_needed_for_holdout} recordings (enough for one hold-out split)", len(dfs) >= n_files_needed_for_holdout, f"{len(dfs)} recordings, so cross-validation is used"),
    ]
    return pd.DataFrame([(c, bool(ok), e) for c, ok, e in rows], columns=["check", "holds", "evidence"])
