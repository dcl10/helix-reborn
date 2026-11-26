# Pipelines

Every model Helix trains is a fitted scikit-learn `Pipeline` (preprocessing steps plus one model), exported to ONNX.
The .NET API runs predictions on the ONNX model and the evaluation worker evaluates it; nothing ever loads the
Python object again (design rule 4 in [architecture.md](architecture.md#design-rules-hard-constraints)). The step
registry and pipeline catalog are added by #22.

## ONNX export

`helix_core.onnx_export` converts a fitted pipeline, checks the ONNX model against scikit-learn, and reads back its
output names. `helix_core.model_metadata` defines the `metadata.json` written next to it. Both follow the contract in
[architecture.md](architecture.md#model-artifacts).

### Inputs and outputs

- **Input:** one tensor named `input`, `float32`, shape `[None, n_features]`, with columns in `metadata.json`'s
  `feature_columns` order. `to_onnx` always passes explicit `initial_types`, never skl2onnx's type inference from
  sample data, so the name is always `input`.
- **Classifiers** are converted with `zipmap=False`, so their outputs are `label` (`int64` class index, `[None]`) and
  `probabilities` (`float32`, `[None, k]`). Without it, probabilities come out as a list of dicts, which the .NET side
  can't read easily. A classifier needs `predict_proba` for a `probabilities` output: `SVC` must be fitted with
  `probability=True`.
- **Regressors** output `variable` (`float32`, `[None, 1]`); `predict_onnx` flattens it to `[n]`.
- The real output names are read from an `onnxruntime` session by `inspect_outputs` and stored in `metadata.json`'s
  `onnx_outputs` (`{"label", "probabilities"}` or `{"value"}`), so consumers never hard-code them.
- Opsets: `TARGET_OPSET = 17` for the default domain, `3` for `ai.onnx.ml`.

### Label encoding

Classification targets can be strings or numbers. Training label-encodes them to `0..k-1` before fitting, so the ONNX
`label` output is always a class index. `metadata.json`'s `classes` lists the original labels in index order
(`classes[label]` is the original value); it's `null` for regression.

### Parity check

`check_parity` runs the scikit-learn pipeline and the ONNX model on the same rows, both on the input cast to `float32`,
and raises `OnnxParityError` if they disagree beyond:

| Problem type | Rule |
| --- | --- |
| Classification, labels | At most 1% of rows predict a different label. |
| Classification, probabilities (when the pipeline has `predict_proba`) | At most 1% of rows have a probability differing by more than `1e-3`. |
| Regression | Every row within `1e-3 × max(1, std of scikit-learn's predictions)`. |

The error says what share of rows failed and names the worst row.

Why rows are allowed to disagree at all: scikit-learn fits in float64, ONNX runs in float32. Casting the input to
float32 for both sides removes most of the difference, but a preprocessing step still rounds differently: scikit-learn
scales a float32 input with float64 parameters and rounds once, while ONNX scales in float32. The result differs in
the last bit (about `6e-8`), and a row that lands exactly on a tree's split threshold goes down the other branch. That
changes its probabilities sharply (by 0.1 or more with few trees) even though its label usually stays the same.

A conversion bug, such as a wrong base score, a reversed class order or a missing `zipmap=False`, shifts most rows, so
it still fails.

**Known limitation:** tree ensembles behind a scaler can exceed the 1% budget. In testing, `XGBClassifier` (10 or
100 trees) behind `MinMaxScaler` or `StandardScaler` flipped 1.5–2% of 200 rows, with every label still agreeing.
Random forest and gradient boosting didn't flip on the same data. A training run hitting this fails its parity check
even though the model is correct. Trees don't need scaling, so leaving the scaler out avoids it.

### Adding a converter

skl2onnx converts scikit-learn's own estimators. Anything else needs a converter registered in
`register_converters()`, which `to_onnx` calls first and which only registers once per process:

```python
update_registered_converter(
    XGBClassifier,
    "XGBoostXGBClassifier",
    calculate_linear_classifier_output_shapes,  # or ..._regressor_... for a regressor
    convert_xgboost,                            # from onnxmltools
    options={"nocl": [True, False], "zipmap": [True, False, "columns"]},
)
```

A classifier's converter must accept the `zipmap` option, since `to_onnx` always passes `zipmap=False`. `XGBClassifier`
and `XGBRegressor` use onnxmltools' XGBoost converter; #22 adds the custom `CorrelationThreshold` converter. Every
step added to the registry needs a test that fits it, converts it and passes `check_parity` (design rule 3).

If a step has no converter, `to_onnx` raises `OnnxConversionError` with skl2onnx's message.

### Model metadata

`ModelMetadata` (pydantic, `extra="forbid"`) is the `metadata.json` schema: `schema_version`, `run_id`,
`problem_type`, `target_column`, `feature_columns`, `id_column`, `classes`, `onnx_input_name` (`"input"`),
`onnx_outputs`, `pipeline` (the `PipelineSpec`), `library_versions` (`library_versions()`: scikit-learn, skl2onnx,
onnx, onnxruntime, xgboost and numpy), `helix_version` and `created_at`. It's exported to
`contracts/model-metadata.schema.json` with the message contracts ([contracts.md](contracts.md)).

### Dependencies

helix-core depends on scikit-learn, skl2onnx, onnx, onnxruntime, onnxmltools and xgboost. scikit-learn is capped
below the first version the resolved skl2onnx isn't tested against: skl2onnx 1.20 tests scikit-learn 1.8, so
`scikit-learn>=1.5,<1.9`. Raise the cap together with skl2onnx once its release notes cover the newer version.

On macOS, XGBoost needs the OpenMP runtime: `brew install libomp` (see
[local-development.md](local-development.md#prerequisites)).
