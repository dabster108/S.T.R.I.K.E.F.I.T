"""Step 7a: the two models that are compared.

- GRU: reads the 20 frames one after the other and remembers what it has seen. This is the main model.
- MLP: a simple network that looks at all 20 frames at once.

Both take normalised features of shape (batch, 20, 22) and return normalised movements of shape (batch, 36).
Both are small enough (under 200,000 parameters) to run on a Quest 2.

For the export, the GRU is rewritten with plain matrix operations (:class:`GRUUnrolled`, with the same weights),
so the ONNX file does not need a GRU operator that Unity might not support.
"""

from __future__ import annotations

from typing import Any

import torch
from torch import nn

from .feature_engineering import N_FEATURES


class GRUForecaster(nn.Module):
    """A GRU reads the window, and a linear layer turns its final state into the 36 predicted movements."""

    def __init__(self, n_in: int, hidden: int, layers: int, n_out: int):
        super().__init__()
        self.gru = nn.GRU(n_in, hidden, num_layers=layers, batch_first=True)
        self.head = nn.Linear(hidden, n_out)

    def forward(self, x: torch.Tensor) -> torch.Tensor:  # x: (B, T, n_in)
        out, _ = self.gru(x)
        return self.head(out[:, -1])


class MLPForecaster(nn.Module):
    """Two hidden layers on the whole window flattened into one list of numbers."""

    def __init__(self, window: int, n_in: int, hidden: int, n_out: int):
        super().__init__()
        self.net = nn.Sequential(nn.Flatten(), nn.Linear(window * n_in, hidden), nn.ReLU(), nn.Linear(hidden, hidden), nn.ReLU(), nn.Linear(hidden, n_out))

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        return self.net(x)


class GRUUnrolled(nn.Module):
    """The same calculation as a one-layer :class:`GRUForecaster`, written step by step with matrix products, sigmoid and tanh only.

    It copies the weights of the trained GRU and loops over the 20 frames of the window. The GRU equations (s = sigmoid) are:
        r = s(W_ir x + b_ir + W_hr h + b_hr)         reset gate
        z = s(W_iz x + b_iz + W_hz h + b_hz)         update gate
        n = tanh(W_in x + b_in + r * (W_hn h + b_hn))  new candidate state
        h = (1 - z) * n + z * h                      next state
    """

    def __init__(self, src: GRUForecaster, window: int):
        super().__init__()
        gru = src.gru
        if gru.num_layers != 1:
            raise ValueError("GRUUnrolled supports one layer")
        self.window, self.hidden = window, gru.hidden_size
        self.w_ih, self.w_hh = nn.Parameter(gru.weight_ih_l0.detach().clone()), nn.Parameter(gru.weight_hh_l0.detach().clone())
        self.b_ih, self.b_hh = nn.Parameter(gru.bias_ih_l0.detach().clone()), nn.Parameter(gru.bias_hh_l0.detach().clone())
        self.head = nn.Linear(src.head.in_features, src.head.out_features)
        self.head.load_state_dict(src.head.state_dict())

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        h_dim = self.hidden
        gi = torch.matmul(x, self.w_ih.t()) + self.b_ih  # input part of all gates for all frames at once, shape (B, T, 3H)
        h = torch.zeros(x.shape[0], h_dim, dtype=x.dtype, device=x.device)
        for t in range(self.window):
            g = gi[:, t]
            gh = torch.matmul(h, self.w_hh.t()) + self.b_hh
            r = torch.sigmoid(g[:, :h_dim] + gh[:, :h_dim])
            z = torch.sigmoid(g[:, h_dim : 2 * h_dim] + gh[:, h_dim : 2 * h_dim])
            n = torch.tanh(g[:, 2 * h_dim :] + r * gh[:, 2 * h_dim :])
            h = (1 - z) * n + z * h
        return self.head(h)


def n_params(model: nn.Module) -> int:
    """Number of trainable parameters."""
    return sum(p.numel() for p in model.parameters() if p.requires_grad)


def build_model(kind: str, cfg: dict[str, Any]) -> nn.Module:
    """Create a 'gru' or an 'mlp' with the sizes from the config file. Raises an error if it is over ``model.max_params``."""
    m, n_out = cfg["model"], 9 * len(cfg["horizons_frames"])
    if kind == "gru":
        model = GRUForecaster(N_FEATURES, m["hidden"], m["layers"], n_out)
    elif kind == "mlp":
        model = MLPForecaster(cfg["window_frames"], N_FEATURES, m["mlp_hidden"], n_out)
    else:
        raise ValueError(f"unknown model type {kind!r}, expected 'gru' or 'mlp'")
    if n_params(model) > m["max_params"]:
        raise ValueError(f"{kind}: {n_params(model)} parameters is more than the limit of {m['max_params']}")
    return model
