# STRIKEFIT motion forecaster

A small neural network that looks at the last 1.0 s of head and two-hand movement and predicts where the head and both hands
will be 0.25, 0.5, 0.75 and 1.0 s later. It is trained on the public Touché sword-strike recordings and exported to ONNX for Unity,
where it drives the optional **Expert** mode of Level 3 (head prediction only, up to 0.75 s; see "How the game uses it" below).

## Result

Mean error in cm over 8 recordings, tested with 5-fold cross-validation by recording (lower is better):

| model | overall error (cm) [95% interval] |
|---|---|
| stay still (baseline) | 46.8 [45.5, 48.1] |
| constant velocity (baseline) | 101.6 [97.7, 105.4] |
| MLP | 31.7 [29.9, 33.3] |
| **GRU (final model)** | **31.0 [29.2, 32.7]** |

The GRU beats the best baseline, but the dataset is tiny (8 recordings, 12 minutes). A full re-run on 23 October 2026 reproduced
the baseline and GRU numbers exactly (the MLP moved by 0.08 cm overall; training on the GPU is not bit-for-bit repeatable). See [models/model_card.md](models/model_card.md) for details and limits.
The full write-up (data, network, training, every metric and what it means) is in [model.md](model.md).

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
scripts/sync_model_to_unity.sh           # copy the model files into Unity and check their SHA-256
scripts/sync_model_to_unity.sh --check   # only check that the Unity copies are identical
```

Running the pipeline rewrites `models/` and `reports/`, and the retrained weights are not bit-for-bit the same as the committed ones.
To reproduce the numbers without touching the shipped model, run it on a copy of this folder.

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

`motion_forecaster.onnx`, `norm.json` and `golden_vector.json` are also in the Unity project, in `Assets/Models/ExpertAI/`.
`scripts/sync_model_to_unity.sh` copies them there (leaving Unity's `.meta` files alone, so the references keep working) and
compares the SHA-256 of every copy.

## Using the model

- **Input** `window`: shape [1, 20, 22]. 20 frames at 20 Hz, the last frame is "now". Features are scaled with `(x - x_mean) / x_std`.
- **Output** `offsets`: shape [1, 36]. Multiply by `y_std` and add `y_mean` to get metres.
- The feature list and the order of the 36 outputs are in `models/model_card.md`.

## How the game uses it

- **Where:** Level 3 only, and only when the player switches Monster AI to **Expert**. Levels 1 and 2 never load the model.
- **Running it:** Unity's Inference Engine (`com.unity.ai.inference` 2.6) on the CPU. The game samples the head and both
  controllers at 20 Hz, rebuilds the same 22 features (`Assets/Scripts/Monster/ExpertAI/ForecastMath.cs`) and runs the model
  10 times a second, spread over a few frames. An EditMode test runs Unity's model on `golden_vector.json` and requires the
  output to match PyTorch within 1e-4 (it passes).
- **What it reads:** only the **head** prediction, up to **0.75 s**. In the game, measured with scripted player motion on the
  same moments (`Captures/ExpertAI/L3_expert_60s.txt`), the head prediction beat "stays still" up to 0.75 s but not at 1.0 s,
  and the hand prediction was worse than "stays still" at every horizon. The hands are behind the config flag
  `useHandPrediction` (off by default) and the horizon behind `maxHorizonSeconds` (0.75 s by default).
- **Fallback:** a missing model, a NaN or infinite output, an exception or slow inference switches Expert off for the rest of
  the fight, and Level 3 plays exactly as in Normal mode.

## Limits

- 8 recordings of lab sword strikes, probably from very few people; STRIKEFIT players dodge and attack, which is different movement.
- Positions only, recorded in 2017 with a different headset; the data has no rotations and the grip value never changes.
- The hand predictions beat "stays still" on this dataset but not in the game, so the game does not use them.
- The model has not been run on a Quest yet, and the Unity-to-dataset axis mapping was chosen with synthetic motion only.

## Data

Touché: Data-Driven Interactive Sword Fighting in Virtual Reality (Dehesa, Vidler, Lutteroth, Padget, CHI 2020), licence CC BY 4.0.
