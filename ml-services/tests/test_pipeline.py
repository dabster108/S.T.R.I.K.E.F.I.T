"""Small tests for each step of the pipeline. They use made-up data, so they need neither the dataset nor a GPU."""

import numpy as np
import pandas as pd
import pytest
import torch

from strikefit_ml.datasets import compute_norm, make_splits, norm_x, pooled_test_files
from strikefit_ml.evaluation import displacement_errors_cm, error_table, predict_constant_velocity, predict_still
from strikefit_ml.feature_engineering import N_FEATURES, make_windows
from strikefit_ml.ingestion import POSITION_COLUMNS, REQUIRED_COLUMNS, ROTATION_COLUMNS, load_config, validate_recording
from strikefit_ml.models import GRUForecaster, GRUUnrolled, build_model
from strikefit_ml.preprocessing import resample_recording, split_at_gaps

CFG = load_config()


def fake_recording(seconds: float = 3.0, step: float = 0.011) -> pd.DataFrame:
    """A recording with a smooth movement and the columns of the real files."""
    t = np.arange(0, seconds, step)
    df = pd.DataFrame({"Time": t})
    for i, col in enumerate(POSITION_COLUMNS):
        df[col] = 100 * np.sin(t * (1 + i * 0.1))
    for col in ROTATION_COLUMNS:
        df[col] = 0.0
    for col in ROTATION_COLUMNS[::4]:  # the w component of every quaternion
        df[col] = 1.0
    df["Grip"] = 1.0
    df["Gesture"] = "strike"
    return df[REQUIRED_COLUMNS]


def test_validation_rejects_time_going_backwards():
    df = fake_recording()
    df.loc[5, "Time"] = 0.0
    with pytest.raises(ValueError, match="strictly increase"):
        validate_recording(df)


def test_resampling_gives_a_constant_rate():
    segments = resample_recording(fake_recording(), rate_hz=20)
    assert len(segments) == 1
    assert np.allclose(np.diff(segments[0]["Time"]), 0.05)


def test_recording_is_cut_at_a_time_gap():
    df = fake_recording()
    df.loc[100:, "Time"] += 5.0  # a jump of about 5 s in the middle
    assert len(split_at_gaps(df["Time"].to_numpy(), 0.1)) == 2
    assert len(resample_recording(df, rate_hz=20)) == 2


def test_windows_have_the_expected_shape_and_stay_inside_the_segment():
    frames = 100
    arr = np.random.default_rng(0).normal(size=(frames, 10))
    w = make_windows(arr, window_frames=20, horizons_frames=[5, 10, 15, 20], stride_frames=2, rate_hz=20)
    assert w["x"].shape[1:] == (20, N_FEATURES)
    assert w["y"].shape[1] == 36
    assert w["t0"].min() >= 19  # a full window of input before "now"
    assert w["t0"].max() + 20 <= frames - 1  # the furthest target is still inside the segment


def test_a_short_segment_gives_no_windows():
    w = make_windows(np.zeros((30, 10)), 20, [5, 10, 15, 20], 2, 20)
    assert len(w["x"]) == 0 and w["y"].shape == (0, 36)


def test_each_recording_is_a_test_recording_exactly_once_and_never_in_two_parts():
    files = [f"file{i}" for i in range(8)]
    splits = make_splits(files, CFG)
    assert len(splits) == CFG["split"]["cv_folds"]
    assert sorted(pooled_test_files(splits)) == sorted(files)
    for s in splits:
        assert len(set(s.files())) == len(s.files())
        assert len(s.val) >= 1


def test_holdout_split_is_used_when_there_are_enough_recordings():
    files = [f"file{i}" for i in range(20)]
    splits = make_splits(files, CFG)
    assert len(splits) == 1 and splits[0].name == "holdout"
    assert len(set(splits[0].files())) == 20


def test_normalisation_uses_the_given_data_and_handles_a_constant_column():
    x = np.random.default_rng(0).normal(5.0, 2.0, size=(50, 20, N_FEATURES)).astype(np.float32)
    x[..., -1] = 1.0  # a column that never changes, like grip
    y = np.random.default_rng(1).normal(size=(50, 36)).astype(np.float32)
    norm = compute_norm(x, y)
    assert norm.x_std[-1] == 1.0
    scaled = norm_x(x, norm)
    assert np.allclose(scaled[..., :-1].mean(axis=(0, 1)), 0, atol=1e-4)
    assert np.allclose(scaled[..., :-1].std(axis=(0, 1)), 1, atol=1e-3)


def test_baselines_and_error_score():
    true = np.ones((4, 36), dtype=np.float32) * 0.01
    still = predict_still(4, 4)
    assert still.shape == (4, 36) and not still.any()
    raw = np.zeros((4, 20, 10))
    assert predict_constant_velocity(raw, 20, [5, 10, 15, 20], 3).shape == (4, 36)
    # a prediction of zero is wrong by sqrt(3) * 1 cm for every point
    err = displacement_errors_cm(still, true)
    assert err.shape == (4, 4, 3) and np.allclose(err, np.sqrt(3))


def test_error_table_has_a_summary_row_and_an_interval_around_the_mean():
    err = np.random.default_rng(0).uniform(1, 3, size=(60, 4, 3))
    file_idx = np.repeat(np.arange(6), 10)
    table = error_table(err, file_idx, CFG)
    row = table[(table.horizon_s == "all") & (table.entity == "all entities")].iloc[0]
    assert row.ci_low <= row.mean_cm <= row.ci_high
    assert np.isclose(row.mean_cm, err.mean())


def test_models_are_small_and_have_the_right_output_shape():
    x = torch.zeros(2, CFG["window_frames"], N_FEATURES)
    for kind in ("gru", "mlp"):
        assert build_model(kind, CFG)(x).shape == (2, 36)


def test_unrolled_gru_gives_the_same_result_as_the_gru():
    torch.manual_seed(0)
    gru = build_model("gru", CFG).eval()
    unrolled = GRUUnrolled(gru, CFG["window_frames"]).eval()
    assert isinstance(gru, GRUForecaster)
    x = torch.randn(5, CFG["window_frames"], N_FEATURES)
    with torch.no_grad():
        assert torch.allclose(gru(x), unrolled(x), atol=1e-5)
