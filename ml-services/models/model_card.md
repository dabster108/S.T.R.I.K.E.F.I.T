# Motion forecaster: model card

**Task.** From the last 1.0 s of head and two-hand positions, predict where each of the three tracked points will be 5 frames (0.25 s), 10 frames (0.50 s), 15 frames (0.75 s), 20 frames (1.00 s) after the last input frame.

**Model.** GRU with 19,236 parameters, saved as `motion_forecaster.onnx` (ONNX opset 15, fixed shapes).

## Inputs and outputs
- Input `window`: float32, shape [1, 20, 22]. This is 20 frames at 20 Hz, and the last frame is "now".
  Every frame has these 22 features, in this order:
  1. head_rel_x
  2. head_rel_y
  3. head_rel_z
  4. controlling_hand_rel_x
  5. controlling_hand_rel_y
  6. controlling_hand_rel_z
  7. secondary_hand_rel_x
  8. secondary_hand_rel_y
  9. secondary_hand_rel_z
  10. head_vel_x
  11. head_vel_y
  12. head_vel_z
  13. controlling_hand_vel_x
  14. controlling_hand_vel_y
  15. controlling_hand_vel_z
  16. secondary_hand_vel_x
  17. secondary_hand_vel_y
  18. secondary_hand_vel_z
  19. controlling_hand_speed
  20. secondary_hand_speed
  21. head_height_z
  22. grip
- Scaling: before running the model, compute `(x - x_mean) / x_std` for every feature. The numbers are in `norm.json`. They were computed from all 8 training recordings.
- Output `offsets`: float32, shape [1, 36], still scaled. To get metres, compute `y * y_std + y_mean` with the numbers from `norm.json`.
  The order is: by horizon (5, 10, 15, 20 frames), then head, controlling hand, secondary hand, then x, y, z.
  Each value is the position at t0 + horizon minus the position at t0, where t0 is the last input frame.
- Units are metres. The axes are those of the training data, with z pointing up. Unity needs to convert its own axes to match.
- `golden_vector.json` has one real example with the expected output, for testing the Unity side.

## Training data
Touché sword-strike recordings (gesture-detection set): 8 recordings, 12.1 minutes, 7,103 windows (one every 2 frames). Licence: CC BY 4.0.
Source: Dehesa, Vidler, Lutteroth, Padget, "Touché: Data-Driven Interactive Sword Fighting in Virtual Reality", CHI 2020.
All recordings were resampled to 20 Hz and converted from centimetres to metres.

## How it was tested
There are only 8 recordings and no participant IDs, so the model was tested with 5-fold cross-validation by recording.
Every recording was used as test data exactly once, by a model that had never seen it (test recordings per fold: [2, 2, 2, 1, 1]).
One more recording per fold was used to decide when to stop training.

The model in this folder was then trained again on all 8 recordings for 16 epochs (the median best epoch of the folds).
It has no test data of its own, so the numbers below describe the cross-validation models, not this exact file.

## Test error
Mean displacement error in cm, over all test windows. The interval comes from a bootstrap that resamples whole recordings.

| model | overall (cm) [95% interval] | 0.25 s | 0.50 s | 0.75 s | 1.00 s |
|---|---|---|---|---|---|
| stay still | 46.79 [45.46, 48.07] | 30.92 | 48.29 | 54.16 | 53.78 |
| constant velocity | 101.64 [97.72, 105.45] | 39.03 | 85.85 | 124.69 | 157.02 |
| GRU | 30.98 [29.24, 32.71] | 18.37 | 30.76 | 36.12 | 38.67 |
| MLP | 31.67 [29.94, 33.33] | 19.04 | 31.26 | 36.82 | 39.55 |

**Does it beat the baselines?** The GRU has an overall error of 31.0 cm. The best baseline (stay still) has 46.8 cm. The model is better, and the 95% intervals do not overlap.

## Known limits
- Very little data: 8 recordings (about 12 minutes) from probably very few people. The intervals are wide, and the model has not seen different bodies or movement styles.
- The recordings show sword strikes by a person standing in a lab. STRIKEFIT uses the model to predict a player who dodges and attacks a monster, so the movement is different.
- Only positions are used, not rotations. The model sees world-frame offsets and the players in the data all face about the same way, so a player facing another way is unfamiliar to it.
- The data was recorded in 2017 with a room-scale headset and controllers. Quest 2 tracking differs in noise, delay and update rate.
- The grip value is always 1 in this dataset, so it carries no information. It is kept so the model's inputs match the game's controllers.
- The ONNX file has not been tested in Unity yet. Check that Unity's Inference Engine supports its operators.
