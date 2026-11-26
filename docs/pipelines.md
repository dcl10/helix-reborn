# Pipelines

Every model Helix trains is a fitted scikit-learn `Pipeline` (preprocessing steps plus one model), exported to ONNX.
The .NET API runs predictions on the ONNX model and the evaluation worker evaluates it; nothing ever loads the
Python object again (design rule 4 in [architecture.md](architecture.md#design-rules-hard-constraints)).

## Pipeline specs

A pipeline is described by a `PipelineSpec` ([architecture.md](architecture.md#pipelinespec)): the problem type, target
and feature columns, the split, zero or more `steps`, one `model`, and an optional hyperparameter `search`. Each step is
`{"step": <id>, "params": {...}}`, where the id is from the registry below.

`helix_core.pipelines` holds the registry (`REGISTRY`, `StepDefinition`), the builder and `CorrelationThreshold`.
`validate_spec(spec)` raises `InvalidSpecError` when:

- a step id is unknown;
- `steps` contains a model, or `model` isn't one;
- a step doesn't support the spec's `problem_type`;
- a step's `params` fail its parameter model (out of range, wrong type, or an unknown key: every model has
  `extra="forbid"`);
- `search.param_grid` has a key that isn't one of the model's parameters, an empty list, or a value that fails that
  parameter's validation;
- there's a `search` with no `param_grid` and the model has no default grid.

`build_pipeline(spec, n_features, n_jobs)` validates the spec, then builds the unfitted
`Pipeline([("s0_<id>", ...), ("s1_<id>", ...), ..., ("model", ...)])`:

- Every estimator that accepts `random_state` gets `split.random_seed`.
- Random forest, XGBoost and k-NN get `n_jobs`, or `1` when there's a search, because the search parallelises instead.
- `select_k_best`'s `k` is clamped to `n_features`.
- MLP `hidden_layer_sizes` lists become tuples.

`search_space(spec)` returns the grid to search, keys prefixed `model__`: `search.param_grid` if given, otherwise the
model's default `search_grid`. MLP layer sizes become tuples here too.

### The catalog

`catalog()` is exported to `contracts/pipeline-catalog.json` by `python -m helix_core.contracts export`
([contracts.md](contracts.md)): `{"schema_version": 1, "steps": [...]}`, each step with its `id`, `kind`, `label`,
`description`, `problem_types`, `params_schema` (the parameter model's JSON Schema: defaults, ranges, enums, and
`additionalProperties: false`) and `search_grid` (`null` for non-models). Steps are sorted by kind, then id, and the
file by key, so regenerating it is deterministic. The API validates specs against it (#24) and the web app builds its
forms from it (#26).

### Steps

Defaults in brackets. "Both" means classification and regression.

| id | Kind | Problem types | Estimator | Parameters | Default search grid |
| --- | --- | --- | --- | --- | --- |
| `simple_imputer` | preprocessor | both | `SimpleImputer` | `strategy` mean, median or most_frequent [median] | |
| `standard_scaler` | preprocessor | both | `StandardScaler` | `with_mean` [true], `with_std` [true] | |
| `mean_centering` | preprocessor | both | `StandardScaler(with_std=False)` | none | |
| `min_max_scaler` | preprocessor | both | `MinMaxScaler` | none | |
| `robust_scaler` | preprocessor | both | `RobustScaler` | `with_centering` [true], `with_scaling` [true] | |
| `variance_threshold` | feature_selector | both | `VarianceThreshold` | `threshold` ≥ 0 [0.01] | |
| `correlation_threshold` | feature_selector | both | `CorrelationThreshold` (below) | `threshold` 0.5–1 [0.9] | |
| `select_k_best` | feature_selector | both | `SelectKBest` with `f_classif` or `f_regression` | `k` ≥ 1 [10], clamped to the number of features | |
| `lasso_selector` | feature_selector | both | `SelectFromModel(Lasso)` | `alpha` > 0 [0.05] | |
| `logistic_regression` | model | classification | `LogisticRegression` | `C` > 0 [1], `fit_intercept` [true], `max_iter` 100–10000 [1000] | `C` 0.01, 0.1, 1, 10; `fit_intercept` both |
| `random_forest_classifier`, `random_forest_regressor` | model | one each | `RandomForest*` | `n_estimators` 10–1000 [100], `max_depth` empty or 1–100 [empty], `min_samples_split` 2–50 [2], `min_samples_leaf` 1–50 [1] | `n_estimators` 10, 50, 100, 200; `max_depth` none, 10, 20, 30; `min_samples_split` 2, 5, 10 |
| `xgboost_classifier`, `xgboost_regressor` | model | one each | `XGB*` | `n_estimators` 10–2000 [100], `max_depth` 1–20 [6], `learning_rate` 0.001–1 [0.3], `subsample` 0.1–1 [1] | `n_estimators` 10, 50, 100, 200; `max_depth` 3, 5, 7, 9; `learning_rate` 0.01, 0.1, 0.3; `subsample` 0.5, 0.8, 1 |
| `svc` | model | classification | `SVC(probability=True)` | `C` > 0 [1], `kernel` linear, poly, rbf or sigmoid [rbf], `degree` 2–5 [3], `gamma` scale or auto [scale] | `kernel` linear, poly, rbf; `C` 0.1, 1, 10; `degree` 2, 3, 4 |
| `svr` | model | regression | `SVR` | as `svc` | as `svc` |
| `knn_classifier`, `knn_regressor` | model | one each | `KNeighbors*` | `n_neighbors` 1–100 [5], `weights` uniform or distance [uniform] | `n_neighbors` 3, 5, 7, 11; `weights` both |
| `mlp_classifier`, `mlp_regressor` | model | one each | `MLP*` | `hidden_layer_sizes` 1–5 layers of 1–1000 [[100]], `activation` identity, logistic, tanh or relu [relu], `alpha` ≥ 0 [0.0001], `learning_rate_init` > 0 [0.001], `max_iter` 50–5000 [500] | `alpha` 0.0001, 0.001, 0.01; `hidden_layer_sizes` [50], [100], [100, 50] |
| `gaussian_nb` | model | classification | `GaussianNB` | `var_smoothing` > 0 [1e-9] | `var_smoothing` 1e-9, 1e-8, 1e-7 |
| `linear_regression` | model | regression | `LinearRegression` | `fit_intercept` [true] | `fit_intercept` both |
| `ridge` | model | regression | `Ridge` | `alpha` ≥ 0 [1], `fit_intercept` [true] | `alpha` 0.01, 0.1, 1, 10 |
| `lasso` | model | regression | `Lasso` | `alpha` > 0 [1], `fit_intercept` [true], `max_iter` ≥ 1 [1000] | `alpha` 0.001, 0.01, 0.1, 1 |
| `elastic_net` | model | regression | `ElasticNet` | `alpha` > 0 [1], `l1_ratio` 0–1 [0.5] | `alpha` 0.01, 0.1, 1; `l1_ratio` 0.2, 0.5, 0.8 |

The random forest, XGBoost, SVM and linear model grids are the original Helix's (`helix/options/choices/ml_models.py`);
the original had no grids for the others. The original's BRNN and MLREM models, Pareto and Poisson scaling, and target
transformations are left out because they can't convert to ONNX.

Every step converts to ONNX (design rule 3). `tests/test_registry.py` builds a minimal pipeline for every step and
problem type it supports, fits it on synthetic data (150 rows, 8 features, seeded), converts it and runs
`check_parity`. Preprocessors and selectors are tested in front of `logistic_regression` or `ridge`. To add a step, add
its parameter model to `params.py` and its `StepDefinition` to `registry.py`, register a converter if skl2onnx has
none, then regenerate the contracts.

### CorrelationThreshold

A Helix selector ported from the original Helix's `run_feature_selection`. `fit` computes the absolute Pearson
correlation matrix pairwise, ignoring missing values (`pd.DataFrame(x).corr().abs()`), and drops each column whose
correlation with any earlier column is above `threshold`. A constant column has no defined correlation, so it's kept.
`threshold` 1.0 keeps every column. It converts to ONNX with skl2onnx's own feature-selection converter and shape
calculator (`convert_sklearn_feature_selection`, `calculate_sklearn_select`), registered in `register_converters()`.


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
and `XGBRegressor` use onnxmltools' XGBoost converter; `CorrelationThreshold` reuses skl2onnx's feature-selection
converter. Every
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
