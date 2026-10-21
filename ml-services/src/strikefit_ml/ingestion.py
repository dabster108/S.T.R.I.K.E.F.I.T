"""Step 1: load the Touché sword-strike recordings and the settings file.

Each recording is a CSV file with one row per tracking sample: the time in seconds, then the position (cm) and rotation
(quaternion, w first) of the head and the two hands, a Grip flag and a Gesture label.
"""

from __future__ import annotations

from pathlib import Path
from typing import Any

import numpy as np
import pandas as pd
import yaml

# This file lives in ml-services/src/strikefit_ml/, so the project root is two folders up.
ROOT = Path(__file__).resolve().parents[2]
CONFIG_PATH = ROOT / "configs" / "motion_forecaster.yaml"

ENTITIES = ("Head", "ControllingHand", "SecondaryHand")
AXES = ("x", "y", "z")
QUAT = ("rotw", "rotx", "roty", "rotz")  # w comes first
POSITION_COLUMNS = [f"{e}_{a}" for e in ENTITIES for a in AXES]
ROTATION_COLUMNS = [f"{e}_{q}" for e in ENTITIES for q in QUAT]
REQUIRED_COLUMNS = ["Time", *POSITION_COLUMNS, *ROTATION_COLUMNS, "Grip", "Gesture"]


def load_config(path: str | Path | None = None) -> dict[str, Any]:
    """Read the settings file (default: configs/motion_forecaster.yaml)."""
    with open(path or CONFIG_PATH, encoding="utf-8") as f:
        return yaml.safe_load(f)


def resolve(cfg: dict[str, Any], key: str) -> Path:
    """Full path of the folder cfg['paths'][key]. Relative paths start at the ml-services folder."""
    p = Path(cfg["paths"][key])
    return p if p.is_absolute() else ROOT / p


def list_recordings(raw_dir: str | Path) -> list[Path]:
    """All recording files in the folder, sorted by name (the names are timestamps)."""
    files = sorted(Path(raw_dir).glob("*.csv"))
    if not files:
        raise FileNotFoundError(f"no recording files found in {raw_dir}")
    return files


def validate_recording(df: pd.DataFrame, name: str = "recording") -> pd.DataFrame:
    """Check the columns, number types, missing values and time order. Return the table unchanged, or raise a ValueError."""
    missing = [c for c in REQUIRED_COLUMNS if c not in df.columns]
    if missing:
        raise ValueError(f"{name}: missing columns {missing}")
    numeric = [c for c in REQUIRED_COLUMNS if c != "Gesture"]
    bad = [c for c in numeric if not pd.api.types.is_numeric_dtype(df[c])]
    if bad:
        raise ValueError(f"{name}: columns are not numeric: {bad}")
    nans = df[REQUIRED_COLUMNS].isna().sum()
    if int(nans.sum()):
        raise ValueError(f"{name}: missing values in {nans[nans > 0].to_dict()}")
    if len(df) < 2:
        raise ValueError(f"{name}: needs at least 2 rows")
    if not np.all(np.diff(df["Time"].to_numpy(dtype=float)) > 0):
        raise ValueError(f"{name}: Time does not strictly increase")
    return df


def load_recording(path: str | Path) -> pd.DataFrame:
    """Read one recording file and check it. Positions stay in cm and quaternions keep w first, as in the file."""
    path = Path(path)
    return validate_recording(pd.read_csv(path), path.name)


def recording_stem(path: str | Path) -> str:
    """Short name of a recording: the file name without its extension."""
    return Path(path).stem
