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

### Validation in the API

The API validates a pipeline when it's saved (`POST`/`PUT /api/experiments/{id}/pipelines…`, see
[api.md](api.md#pipelines)), against `contracts/pipeline-catalog.json` and the experiment's dataset, so a saved
pipeline doesn't fail on these when it's run. It applies helix-core's rules, both `PipelineSpec`'s and
`validate_spec`'s, and adds checks against the dataset's columns:

- `problem_type` is `classification` or `regression`.
- `target_column` is a dataset column and isn't a feature. For regression it must be numeric.
- `feature_columns` has at least one column, no duplicates, and each is a dataset column whose inferred type is
  `numeric`.
- `id_column`, if given, is a dataset column and isn't a feature.
- `split.test_size` is 0.05–0.5, and `split.cv_folds` is null or 2–10.
- Every `steps[i].step` and `model.step` is in the catalog. Steps aren't models and the model is one, each supports the
  `problem_type`, and its `params` pass its `params_schema` (so unknown parameters are rejected).
- `search`, if given: `method` is `grid` or `random`, `cv_folds` is 2–10, and `n_iter` is required for `random` and is
  1–500. `param_grid` keys are model parameters, each with a non-empty list of candidates that each pass that
  parameter's schema. Without a `param_grid`, the model must have a default `search_grid`.
- Unknown fields anywhere in the spec are rejected.

Errors name their path: `feature_columns: 'site' is not numeric`, `model.params.n_estimators: 0 should be at least 10`,
`search.param_grid.alpha[1]: -1 should be at least 0`.

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


## Training

The training worker (`services/training`, package `helix-training`, command `helix-training`) consumes `TrainingJob`s
from `training.jobs`. `TrainingWorker` subclasses `helix_core.worker.Worker`, which handles the queue, events,
ack/reject, health and SIGTERM. Its `handle` loads the CSV, calls `helix_training.train.train` and uploads the results.
`train` is pure (no AMQP or S3), so it's tested directly.

### Algorithm

1. Re-validate the `PipelineSpec` (it's validated when the job is parsed; this catches a job built another way), then
   `validate_spec` it against the registry. Select the feature, target and ID columns (`select_features_target`:
   features must exist and be numeric).
2. Drop rows with no target value, remembering each remaining row's position in the CSV. At least 20 rows must be left.
3. **Classification:** label-encode the target. Non-numeric targets are encoded as strings; numeric ones keep their
   type. There must be at least 2 classes, and every class needs at least 2 rows. `classes` lists the original labels
   in index order. **Regression:** the target must be numeric.
4. Split with `train_test_split` (`split.test_size`, `random_state=split.random_seed`, shuffled, stratified for
   classification). Progress 0.1.
5. k-NN only: `n_neighbors` (every value, when searching) must be at most the fewest rows the model is fitted on: the
   training rows, or a training fold of the search's or cross-validation's folds. Otherwise `InvalidSpecError`.
   scikit-learn only notices at prediction time, and ONNX Runtime fails with an internal error.
6. With `search`: `GridSearchCV`, or `RandomizedSearchCV` (`n_iter`, `random_state=seed`), over `search_space(spec)`,
   `cv` = `StratifiedKFold` (classification) or `KFold` (regression) with `search.cv_folds` folds, shuffled and seeded.
   It parallelises over folds (`n_jobs`), so the model gets one core, refits on the best parameters, and reports
   `best_params` without the `model__` prefix (MLP layer sizes as lists). Progress 0.5.
7. With `split.cv_folds`: `cross_validate` a clone of the final configuration on the training rows with the same kind
   of splitter. The clone's model gets one core, since the folds run in parallel. Progress 0.7.
8. Without a search, fit the pipeline on the training rows (the search has already refit it).
9. Export with `to_onnx` and `check_parity` on the test rows. Progress 0.85.
10. Build `ModelMetadata`, with the output names from `inspect_outputs`.

scikit-learn's `ValueError`s while splitting, searching, cross-validating or fitting come from the data or spec meeting
the estimator, so they're reported as `InvalidDataError` with the stage named. Like every `HelixError`, they're
published as a `failed` event and the job is acked, not dead-lettered. So is `OnnxParityError`. When a scaler sits
before a tree model with nothing scale-sensitive in between (`helix_core.pipelines.redundant_scaler_steps`), its
message adds: *Tree models (random forest, XGBoost) don't need scaling, and a scaler before one can make the exported
model differ slightly from the trained one. Try removing '<step>'.* The web app's pipeline builder (#26) warns about the
same thing with the same rule.

### Metrics

Cross-validation reports `{"mean", "std"}` across folds for each metric:

| Problem type | Metrics |
| --- | --- |
| Classification | `accuracy`, `f1_weighted`, `precision_weighted`, `recall_weighted`, `roc_auc` (scikit-learn's `roc_auc` for two classes, `roc_auc_ovr_weighted` for more, reported as `roc_auc` either way) |
| Regression | `r2`, `mae`, `rmse` (scikit-learn's negated scorers, flipped back to positive) |

The search picks its best parameters on `f1_weighted` (classification) or `r2` (regression). Test-set metrics are the
evaluation worker's job (#28).

### Outputs

Under the job's `output_prefix` ([architecture.md](architecture.md#storage-layout-s3)):

| Key | Content |
| --- | --- |
| `model.onnx` | The fitted pipeline (`application/octet-stream`) |
| `metadata.json` | `ModelMetadata` ([below](#model-metadata)) |
| `split.json` | `{"train_index": [...], "test_index": [...]}`: 0-based positions of the rows in the uploaded CSV, so the evaluation worker can re-read the CSV and pick the same rows. Rows with no target are in neither. |
| `cv_metrics.json` | The cross-validation metrics; only when `split.cv_folds` is set |

The `succeeded` event's `result` is `{"model_key", "metadata_key", "split_key", "cv_metrics_key" (or null),
"cv_metrics" (or null), "best_params" (or null), "train_rows", "test_rows"}`. Progress events go 0.1, 0.5 (search),
0.7 (cross-validation), 0.85, 0.95 (uploaded).

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
