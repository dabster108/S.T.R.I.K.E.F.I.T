"""STRIKEFIT motion forecasting: predict where the head and both hands will be 0.25 to 1.0 s from now.

The pipeline has nine steps. Each step is a module, and all of them read their settings from configs/motion_forecaster.yaml:

    1. ingestion            load the recordings
    2. data_audit           check the data
    3. preprocessing        clean and resample the data
    4. feature_engineering  build windows, features and targets
    5. datasets             split by recording and normalise
    6. evaluation           score the baselines (and, in step 8, the trained models)
    7. models, training     train the models
    8. evaluation           evaluate on the test recordings
    9. export               train the final model and save it in models/

pipeline.py runs all nine steps in order.
"""

__version__ = "0.1.0"
