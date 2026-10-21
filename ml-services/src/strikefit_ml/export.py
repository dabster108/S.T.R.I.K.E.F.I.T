"""Step 9: save the final model and everything needed to use it in the ``models/`` folder.

Files written to ``models/``:
    motion_forecaster.onnx   the model for Unity
    motion_forecaster.pt     the PyTorch weights (to continue working in Python)
    norm.json                numbers to scale the input and un-scale the output
    golden_vector.json       one real example with its expected output, to test the Unity side
    model_card.md            what the model does, how good it is, and its limits
    training_config.yaml     the settings used for training

The ONNX model takes one window ``window`` of shape [1, 20, 22] (normalised features) and returns ``offsets`` of shape
[1, 36] (normalised movements). The shapes are fixed and the GRU is exported through :class:`models.GRUUnrolled`,
so the file contains only basic operators (MatMul, Add, Sigmoid, Tanh, ...) and no GRU operator.
"""

from __future__ import annotations

import json
import shutil
from collections import Counter
from pathlib import Path
from typing import Any

import numpy as np
import torch
from torch import nn

from .datasets import Norm, denorm_y, norm_x, save_norm
from .feature_engineering import FEATURE_NAMES, N_FEATURES, target_names
from .ingestion import CONFIG_PATH
from .models import GRUForecaster, GRUUnrolled
from .training import save_checkpoint

INPUT_NAME, OUTPUT_NAME = "window", "offsets"


def save_final_files(model: nn.Module, norm: Norm, cfg: dict[str, Any], models_dir: str | Path, kind: str, epochs: int, files: list[str]) -> None:
    """Save the weights, the normalisation numbers and a copy of the config file in the models folder."""
    out, names = Path(models_dir), cfg["export"]
    save_checkpoint(model, out / names["weights_file"], {"kind": kind, "epochs": epochs, "trained_on_files": files})
    save_norm(norm, out / names["norm_file"], {"computed_from_files": files, "note": "computed from ALL recordings, because the final model is trained on all of them"})
    shutil.copy(CONFIG_PATH, out / names["config_file"])


def exportable(model: nn.Module, cfg: dict[str, Any]) -> nn.Module:
    """The module that gets exported: the unrolled copy for a GRU, the model itself for an MLP."""
    return GRUUnrolled(model, cfg["window_frames"]).eval() if isinstance(model, GRUForecaster) else model.eval()


def export_onnx(model: nn.Module, cfg: dict[str, Any], path: str | Path) -> Path:
    """Write the ONNX file. Input shape [1, window, 22], output shape [1, 36], no dynamic shapes."""
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    dummy = torch.zeros(1, cfg["window_frames"], N_FEATURES, dtype=torch.float32)
    torch.onnx.export(
        exportable(model.cpu(), cfg), dummy, str(path),
        input_names=[INPUT_NAME], output_names=[OUTPUT_NAME], opset_version=cfg["export"]["opset"], dynamic_axes=None, dynamo=False,
    )
    return path


def onnx_ops(path: str | Path) -> dict[str, int]:
    """The operator types used in the ONNX file, with how often each appears."""
    import onnx

    graph = onnx.load(str(path)).graph
    return dict(sorted(Counter(node.op_type for node in graph.node).items()))


def onnx_io_shapes(path: str | Path) -> dict[str, Any]:
    """Input and output shapes and the opset version, as written in the ONNX file."""
    import onnx

    model = onnx.load(str(path))

    def shape(t) -> list:
        return [d.dim_value if d.HasField("dim_value") else d.dim_param for d in t.type.tensor_type.shape.dim]

    return {
        "inputs": {i.name: shape(i) for i in model.graph.input},
        "outputs": {o.name: shape(o) for o in model.graph.output},
        "opset": [o.version for o in model.opset_import],
    }


def run_onnx(path: str | Path, x_norm: np.ndarray) -> np.ndarray:
    """Run the ONNX model on normalised windows of shape (n, T, 22), one window at a time (the model has batch size 1)."""
    import onnxruntime as ort

    session = ort.InferenceSession(str(path), providers=["CPUExecutionProvider"])
    outputs = [session.run([OUTPUT_NAME], {INPUT_NAME: x_norm[i : i + 1].astype(np.float32)})[0] for i in range(len(x_norm))]
    return np.concatenate(outputs)


@torch.no_grad()
def parity(model: nn.Module, path: str | Path, x_norm: np.ndarray) -> float:
    """Largest difference between the PyTorch output and the ONNX output on the given windows. It should be tiny."""
    reference = model.cpu().eval()(torch.from_numpy(x_norm.astype(np.float32))).numpy()
    return float(np.abs(reference - run_onnx(path, x_norm)).max())


def golden_vector(model: nn.Module, norm: Norm, raw_window: np.ndarray, raw_features: np.ndarray, cfg: dict[str, Any], path: str | Path,
                  onnx_path: str | Path | None = None) -> dict[str, Any]:
    """Write one real input window together with the output the exported model must give for it.

    The Unity side can use this file to check that it computes the same features and the same prediction.
    ``raw_window`` has shape (T, 10): positions in metres plus grip. ``raw_features`` has shape (T, 22): the features computed from it.
    """
    x_norm = norm_x(raw_features[None], norm)
    with torch.no_grad():
        y_norm = model.cpu().eval()(torch.from_numpy(x_norm)).numpy()
    y_m = denorm_y(y_norm, norm)
    out = {
        "description": "One real input window and the output the exported model must return for it. In Unity: build the features from raw_window_m "
        "and compare them with features_raw, normalise them, run the model, and compare the result with expected_output_normalised "
        "(allowed difference: see tolerance). Then un-normalise the result and compare it with expected_offsets_m.",
        "window_columns": ["head_x", "head_y", "head_z", "controlling_x", "controlling_y", "controlling_z", "secondary_x", "secondary_y", "secondary_z", "grip"],
        "raw_window_m": np.round(raw_window, 6).tolist(),
        "feature_names": FEATURE_NAMES,
        "features_raw": np.round(raw_features, 6).tolist(),
        "features_normalised": np.round(x_norm[0], 6).tolist(),
        "expected_output_normalised": np.round(y_norm[0], 6).tolist(),
        "target_names": target_names(cfg["horizons_frames"]),
        "expected_offsets_m": np.round(y_m[0], 6).tolist(),
        "sample_rate_hz": cfg["sample_rate_hz"],
        "tolerance": cfg["export"]["parity_tolerance"],
    }
    if onnx_path is not None:
        out["onnx_output_normalised"] = np.round(run_onnx(onnx_path, x_norm)[0], 6).tolist()
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    Path(path).write_text(json.dumps(out), encoding="utf-8")
    return out


def write_model_card(path: str | Path, info: dict[str, Any]) -> None:
    """Write the model card (a short document about the model) from the numbers produced by the evaluation.

    ``info`` holds: cfg, results, verdict, exported_kind, n_params, opset, n_files, minutes, n_windows, fold_sizes, final_epochs.
    """
    cfg, results = info["cfg"], info["results"]
    names = cfg["export"]
    rate = cfg["sample_rate_hz"]
    horizons = ", ".join(f"{h} frames ({h / rate:.2f} s)" for h in cfg["horizons_frames"])
    feature_list = "\n".join(f"  {i + 1}. {name}" for i, name in enumerate(FEATURE_NAMES))
    header = "| model | overall (cm) [95% interval] | " + " | ".join(f"{h / rate:.2f} s" for h in cfg["horizons_frames"]) + " |"
    divider = "|---|---|" + "---|" * len(cfg["horizons_frames"])
    rows = "\n".join(
        f"| {name} | {v['overall']:.2f} [{v['low']:.2f}, {v['high']:.2f}] | " + " | ".join(f"{x:.2f}" for x in v["per_horizon"]) + " |"
        for name, v in results.items()
    )
    text = f"""# Motion forecaster: model card

**Task.** From the last 1.0 s of head and two-hand positions, predict where each of the three tracked points will be {horizons} after the last input frame.

**Model.** {info['exported_kind'].upper()} with {info['n_params']:,} parameters, saved as `{names['onnx_file']}` (ONNX opset {info['opset']}, fixed shapes).

## Inputs and outputs
- Input `{INPUT_NAME}`: float32, shape [1, {cfg['window_frames']}, {N_FEATURES}]. This is {cfg['window_frames']} frames at {rate} Hz, and the last frame is "now".
  Every frame has these {N_FEATURES} features, in this order:
{feature_list}
- Scaling: before running the model, compute `(x - x_mean) / x_std` for every feature. The numbers are in `{names['norm_file']}`. They were computed from all {info['n_files']} training recordings.
- Output `{OUTPUT_NAME}`: float32, shape [1, {len(cfg['horizons_frames']) * 9}], still scaled. To get metres, compute `y * y_std + y_mean` with the numbers from `{names['norm_file']}`.
  The order is: by horizon ({', '.join(str(h) for h in cfg['horizons_frames'])} frames), then head, controlling hand, secondary hand, then x, y, z.
  Each value is the position at t0 + horizon minus the position at t0, where t0 is the last input frame.
- Units are metres. The axes are those of the training data, with z pointing up. Unity needs to convert its own axes to match.
- `{names['golden_vector_file']}` has one real example with the expected output, for testing the Unity side.

## Training data
Touché sword-strike recordings (gesture-detection set): {info['n_files']} recordings, {info['minutes']:.1f} minutes, {info['n_windows']:,} windows (one every {cfg['stride_frames']} frames). Licence: CC BY 4.0.
Source: Dehesa, Vidler, Lutteroth, Padget, "Touché: Data-Driven Interactive Sword Fighting in Virtual Reality", CHI 2020.
All recordings were resampled to {rate} Hz and converted from centimetres to metres.

## How it was tested
There are only {info['n_files']} recordings and no participant IDs, so the model was tested with {cfg['split']['cv_folds']}-fold cross-validation by recording.
Every recording was used as test data exactly once, by a model that had never seen it (test recordings per fold: {info['fold_sizes']}).
One more recording per fold was used to decide when to stop training.

The model in this folder was then trained again on all {info['n_files']} recordings for {info['final_epochs']} epochs (the median best epoch of the folds).
It has no test data of its own, so the numbers below describe the cross-validation models, not this exact file.

## Test error
Mean displacement error in cm, over all test windows. The interval comes from a bootstrap that resamples whole recordings.

{header}
{divider}
{rows}

{info['verdict']}

## Known limits
- Very little data: {info['n_files']} recordings (about {info['minutes']:.0f} minutes) from probably very few people. The intervals are wide, and the model has not seen different bodies or movement styles.
- The recordings show sword strikes by a person standing in a lab. STRIKEFIT uses the model to predict a player who dodges and attacks a monster, so the movement is different.
- Only positions are used, not rotations. The model sees world-frame offsets and the players in the data all face about the same way, so a player facing another way is unfamiliar to it.
- The data was recorded in 2017 with a room-scale headset and controllers. Quest 2 tracking differs in noise, delay and update rate.
- The grip value is always 1 in this dataset, so it carries no information. It is kept so the model's inputs match the game's controllers.
- The ONNX file has not been tested in Unity yet. Check that Unity's Inference Engine supports its operators.
"""
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    Path(path).write_text(text, encoding="utf-8")
