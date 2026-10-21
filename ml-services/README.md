# STRIKEFIT motion forecaster

A small neural network that looks at the last 1.0 s of head and two-hand movement and predicts where the head and both hands
will be 0.25, 0.5, 0.75 and 1.0 s later. It is trained on the public Touché sword-strike recordings and exported to ONNX for Unity.

## Result

Mean error in cm over 8 recordings, tested with 5-fold cross-validation by recording (lower is better):

| model | overall error (cm) [95% interval] |
|---|---|
| stay still (baseline) | 46.8 [45.5, 48.1] |
| constant velocity (baseline) | 101.6 [97.7, 105.4] |
| MLP | 31.7 [29.9, 33.3] |
| **GRU (final model)** | **31.0 [29.2, 32.7]** |

The GRU beats the best baseline, but the dataset is tiny (8 recordings, 12 minutes). See [models/model_card.md](models/model_card.md) for details and limits.

## The pipeline

| step | what happens | code | notebook |
|---|---|---|---|
| 1 | Load the recordings and check them | `ingestion.py` | 01 |
| 2 | Check units, axes, time steps and rotations | `data_audit.py` | 01 |
| 3 | Cut at time jumps, resample to 20 Hz, convert to metres | `preprocessing.py` | 02 |
| 4 | Build windows (20 frames), 22 features per frame and 36 targets | `feature_engineering.py` | 03 |
| 5 | Split by recording and normalise with training data only | `datasets.py` | 04 |
| 6 | Score two baselines that need no training | `evaluation.py` | 04 |
| 7 | Train a GRU and an MLP on every cross-validation fold | `models.py`, `training.py` | 05 |
| 8 | Evaluate on the test recordings, with confidence intervals | `evaluation.py` | 06 |
| 9 | Train the final model on all recordings and export it | `export.py` | 06 |

All code is in `src/strikefit_ml/`, and all settings are in `configs/motion_forecaster.yaml`.

## How to run

```bash
uv sync                                  # install the dependencies
uv run python -m strikefit_ml.pipeline   # run all nine steps (about 30 seconds)
uv run pytest                            # run the tests
uv run jupyter lab                       # open the notebooks to see each step with plots
```

The raw data is not committed. Put the Touché gesture-detection data in `data/raw/touche_sword_strikes/` (see `configs/motion_forecaster.yaml` for the exact path).

## Folders

```
configs/     settings for the whole pipeline
data/        raw recordings and the cleaned data (not committed)
src/         the code, one module per step
notebooks/   one notebook per step, with plots
tests/       small tests that need no dataset
reports/     result tables and figures
models/      the final model and everything needed to use it
```

### The `models/` folder

| file | what it is |
|---|---|
| `motion_forecaster.onnx` | the model for Unity |
| `motion_forecaster.pt` | the PyTorch weights |
| `norm.json` | numbers to scale the input and the output |
| `golden_vector.json` | one real example with its expected output, to test the Unity side |
| `model_card.md` | what the model does, how good it is, and its limits |
| `training_config.yaml` | the settings used for training |
| `cv_folds/` | the models of the cross-validation, used only for the evaluation |

## Using the model

- **Input** `window`: shape [1, 20, 22]. 20 frames at 20 Hz, the last frame is "now". Features are scaled with `(x - x_mean) / x_std`.
- **Output** `offsets`: shape [1, 36]. Multiply by `y_std` and add `y_mean` to get metres.
- The feature list and the order of the 36 outputs are in `models/model_card.md`.

## Data

Touché: Data-Driven Interactive Sword Fighting in Virtual Reality (Dehesa, Vidler, Lutteroth, Padget, CHI 2020), licence CC BY 4.0.
