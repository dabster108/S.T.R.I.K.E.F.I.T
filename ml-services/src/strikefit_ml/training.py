"""Step 7b: train the models.

Training uses the AdamW optimiser and the Huber loss on normalised targets. The Huber loss is less sensitive to a few
very large errors than the squared error. Training stops early when the loss on the validation recordings has not improved
for ``train.patience`` epochs, and the best weights seen so far are kept. All random choices use the seed from the config file.
"""

from __future__ import annotations

import copy
import random
import time
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

import numpy as np
import pandas as pd
import torch
from torch import nn
from torch.utils.data import DataLoader
from tqdm import tqdm

from .datasets import Norm, Split, check_split, compute_norm, denorm_y, load_norm, make_loader, norm_x, pooled_test_files, save_norm, stack
from .models import build_model


def set_seed(seed: int) -> None:
    """Make the random numbers of Python, NumPy and PyTorch repeatable."""
    random.seed(seed)
    np.random.seed(seed)
    torch.manual_seed(seed)


def pick_device() -> torch.device:
    """Use the Apple GPU (MPS) if there is one, otherwise the CPU."""
    return torch.device("mps" if torch.backends.mps.is_available() else "cpu")


@dataclass
class History:
    """What happened during one training run."""

    train_loss: list[float] = field(default_factory=list)  # one value per epoch
    val_loss: list[float] = field(default_factory=list)
    best_epoch: int = 0  # counted from 1
    best_val: float = float("inf")
    epochs_run: int = 0
    seconds: float = 0.0
    stopped_early: bool = False
    device: str = "cpu"


def _run_epoch(model: nn.Module, loader: DataLoader, loss_fn: nn.Module, device: torch.device, optimiser: torch.optim.Optimizer | None) -> float:
    """Go once through all the data. The model learns only if an ``optimiser`` is given. Returns the average loss per window."""
    model.train(optimiser is not None)
    total, count = 0.0, 0
    with torch.set_grad_enabled(optimiser is not None):
        for xb, yb in loader:
            xb, yb = xb.to(device), yb.to(device)
            loss = loss_fn(model(xb), yb)
            if optimiser is not None:
                optimiser.zero_grad(set_to_none=True)
                loss.backward()
                optimiser.step()
            total += float(loss.detach()) * len(xb)
            count += len(xb)
    return total / max(count, 1)


def fit(model: nn.Module, train_loader: DataLoader, val_loader: DataLoader | None, cfg: dict[str, Any], device: torch.device | None = None,
        fixed_epochs: int | None = None, desc: str = "train", progress: bool = True) -> History:
    """Train ``model`` in place. Afterwards it holds the weights with the lowest validation loss.

    Without a ``val_loader`` there is nothing to stop on, so the model trains for exactly ``fixed_epochs`` epochs.
    That is used for the final model, which is trained on all recordings.
    """
    settings = cfg["train"]
    if settings["loss"] != "huber":
        raise ValueError("only the Huber loss is supported")
    device = device or pick_device()
    model.to(device)
    optimiser = torch.optim.AdamW(model.parameters(), lr=settings["lr"], weight_decay=settings["weight_decay"])
    loss_fn = nn.HuberLoss(delta=settings["huber_delta"])

    history = History(device=str(device))
    best_state = copy.deepcopy(model.state_dict())
    start = time.perf_counter()
    max_epochs = fixed_epochs or settings["max_epochs"]
    bar = tqdm(range(1, max_epochs + 1), desc=desc, disable=not progress, leave=False)
    epochs_without_improvement = 0

    for epoch in bar:
        train_loss = _run_epoch(model, train_loader, loss_fn, device, optimiser)
        history.train_loss.append(train_loss)
        history.epochs_run = epoch
        if val_loader is None:
            history.best_epoch, history.best_val = epoch, float("nan")
            bar.set_postfix(train=f"{train_loss:.4f}")
            continue

        val_loss = _run_epoch(model, val_loader, loss_fn, device, None)
        history.val_loss.append(val_loss)
        bar.set_postfix(train=f"{train_loss:.4f}", val=f"{val_loss:.4f}")
        if val_loss < history.best_val - 1e-9:
            history.best_val, history.best_epoch = val_loss, epoch
            epochs_without_improvement = 0
            best_state = copy.deepcopy(model.state_dict())
        else:
            epochs_without_improvement += 1
            if epochs_without_improvement >= settings["patience"]:
                history.stopped_early = True
                break

    if val_loader is not None:
        model.load_state_dict(best_state)
    history.seconds = time.perf_counter() - start
    return history


@torch.no_grad()
def predict_normalised(model: nn.Module, x_norm: np.ndarray, device: torch.device | None = None, batch_size: int = 2048) -> np.ndarray:
    """Run the model on normalised windows of shape (n, T, 22). The output is still normalised."""
    device = device or next(model.parameters()).device
    model.eval().to(device)
    outputs = [model(torch.from_numpy(x_norm[i : i + batch_size]).to(device)).cpu().numpy() for i in range(0, len(x_norm), batch_size)]
    return np.concatenate(outputs) if outputs else np.zeros((0, 0), np.float32)


def train_on_files(kind: str, cfg: dict[str, Any], windows: dict[str, dict[str, np.ndarray]], train: list[str], val: list[str],
                   device: torch.device | None = None, progress: bool = True, desc: str = "") -> tuple[nn.Module, Norm, History]:
    """Train a new model on the ``train`` recordings and use the ``val`` recordings to decide when to stop.

    The normalisation numbers come from the training recordings only.
    """
    set_seed(cfg["seed"])
    tr, va = stack(windows, train), stack(windows, val)
    norm = compute_norm(tr["x"], tr["y"])
    model = build_model(kind, cfg)
    batch_size = cfg["train"]["batch_size"]
    train_loader = make_loader(tr["x"], tr["y"], norm, batch_size, True, cfg["seed"])
    val_loader = make_loader(va["x"], va["y"], norm, batch_size, False)
    history = fit(model, train_loader, val_loader, cfg, device, desc=desc or kind, progress=progress)
    return model, norm, history


def save_checkpoint(model: nn.Module, path: str | Path, extra: dict[str, Any] | None = None) -> None:
    """Save the model weights, plus any extra information (``extra``) such as the model type."""
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    torch.save({"state_dict": model.state_dict(), **(extra or {})}, path)


def load_checkpoint(kind: str, cfg: dict[str, Any], path: str | Path) -> nn.Module:
    """Create a model of type ``kind`` and load saved weights into it (on the CPU)."""
    model = build_model(kind, cfg)
    model.load_state_dict(torch.load(path, map_location="cpu", weights_only=True)["state_dict"])
    return model.eval()


def cross_validate(kind: str, cfg: dict[str, Any], windows: dict[str, dict[str, np.ndarray]], splits: list[Split], out_dir: str | Path,
                   device: torch.device | None = None, progress: bool = True) -> list[History]:
    """Train one model per split. Saves each model as ``<kind>_<split>.pt`` and its normalisation as ``norm_<split>.json``."""
    out = Path(out_dir)
    histories = []
    for s in splits:
        check_split(s)
        model, norm, history = train_on_files(kind, cfg, windows, list(s.train), list(s.val), device, progress, desc=f"{kind} {s.name}")
        save_checkpoint(model, out / f"{kind}_{s.name}.pt", {"kind": kind, "best_epoch": history.best_epoch})
        save_norm(norm, out / f"norm_{s.name}.json", {"computed_from_files": list(s.train), "split": s.name})
        histories.append(history)
    return histories


def pooled_predictions(kind: str, cfg: dict[str, Any], windows: dict[str, dict[str, np.ndarray]], splits: list[Split], out_dir: str | Path) -> dict[str, np.ndarray]:
    """Predict the test windows of every split with the model of that split, and join the results.

    Returns ``pred`` and ``true`` (n, 36, in metres), ``raw`` (n, T, 10), ``file_idx`` (n,) and ``files`` (the test recordings, in order).
    """
    out = Path(out_dir)
    files = pooled_test_files(splits)
    preds, trues, raws, file_idx = [], [], [], []
    for s in splits:
        model = load_checkpoint(kind, cfg, out / f"{kind}_{s.name}.pt")
        norm = load_norm(out / f"norm_{s.name}.json")
        test = stack(windows, list(s.test))
        pred_norm = predict_normalised(model, norm_x(test["x"], norm), torch.device("cpu"))
        preds.append(denorm_y(pred_norm, norm))
        trues.append(test["y"])
        raws.append(test["raw"])
        file_idx.append(np.array([files.index(s.test[i]) for i in test["file_idx"]]))
    return {"pred": np.concatenate(preds), "true": np.concatenate(trues), "raw": np.concatenate(raws), "file_idx": np.concatenate(file_idx), "files": np.array(files)}


def final_refit(kind: str, cfg: dict[str, Any], windows: dict[str, dict[str, np.ndarray]], files: list[str], epochs: int,
                device: torch.device | None = None, progress: bool = True) -> tuple[nn.Module, Norm, History]:
    """Train the final model on ALL recordings for exactly ``epochs`` epochs.

    There is no validation data left at this point, so the number of epochs comes from the cross-validation runs.
    """
    set_seed(cfg["seed"])
    data = stack(windows, files)
    norm = compute_norm(data["x"], data["y"])
    model = build_model(kind, cfg)
    loader = make_loader(data["x"], data["y"], norm, cfg["train"]["batch_size"], True, cfg["seed"])
    history = fit(model, loader, None, cfg, device, fixed_epochs=epochs, desc=f"{kind} final", progress=progress)
    return model, norm, history


def summarise_histories(histories: dict[str, list[History]], splits: list[Split]) -> pd.DataFrame:
    """One row per model and fold with the main numbers of each training run."""
    rows = [
        {
            "model": kind,
            "fold": s.name,
            "epochs_run": h.epochs_run,
            "best_epoch": h.best_epoch,
            "best_val_loss": h.best_val,
            "final_train_loss": h.train_loss[-1],
            "stopped_early": h.stopped_early,
            "seconds": h.seconds,
            "device": h.device,
        }
        for kind, hs in histories.items()
        for s, h in zip(splits, hs)
    ]
    return pd.DataFrame(rows)


def pick_final_model(summary: pd.DataFrame) -> tuple[str, int]:
    """Choose the model type with the lowest average validation loss (not the test error, which must stay untouched).

    Also returns the number of epochs for the final training: the median best epoch of that model's folds.
    """
    mean_val = summary.groupby("model").best_val_loss.mean()
    kind = str(mean_val.idxmin())
    epochs = int(np.median(summary[summary.model == kind].best_epoch))
    return kind, epochs


def plot_training_curves(histories: dict[str, list[History]], path: str | Path) -> None:
    """Plot training and validation loss per epoch. One panel per model type, one pair of lines per fold."""
    import matplotlib

    matplotlib.use("Agg", force=False)
    import matplotlib.pyplot as plt

    fig, axes = plt.subplots(1, len(histories), figsize=(6 * len(histories), 4), sharey=True, squeeze=False)
    for ax, (name, hs) in zip(axes[0], histories.items()):
        for i, h in enumerate(hs):
            ax.plot(range(1, len(h.train_loss) + 1), h.train_loss, color=f"C{i}", alpha=0.6, lw=1.2, label=f"fold {i} train")
            if h.val_loss:
                ax.plot(range(1, len(h.val_loss) + 1), h.val_loss, color=f"C{i}", ls="--", lw=1.2, label=f"fold {i} validation")
                ax.plot(h.best_epoch, h.best_val, "o", color=f"C{i}")
        ax.set_title(f"{name}: loss during training (dot = best epoch)")
        ax.set_xlabel("epoch")
        ax.grid(alpha=0.3)
    axes[0][0].set_ylabel("Huber loss (normalised targets)")
    axes[0][-1].legend(fontsize=7, ncol=2)
    fig.tight_layout()
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    fig.savefig(path, dpi=130)
    plt.close(fig)
