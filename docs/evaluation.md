# Evaluation

The evaluation worker (`services/evaluation`, package `helix-evaluation`, command `helix-evaluation`) consumes
`EvaluationJob`s from `evaluation.jobs`. `EvaluationWorker` subclasses `helix_core.worker.Worker`, which handles the
queue, events, ack/reject, health and SIGTERM. It evaluates a trained model **using its ONNX file**, never the
pickled scikit-learn object (design rule 4 in [architecture.md](architecture.md)), so the same code path that scores
a model in production is the one evaluation reports on.

## Inputs

`EvaluationJob` ([architecture.md](architecture.md#messaging)) names three things already written by the training
worker under `model_prefix` ([pipelines.md](pipelines.md#outputs)), plus the dataset:

| Key | Content |
| --- | --- |
| `{model_prefix}metadata.json` | `ModelMetadata`: feature/target columns, classes, ONNX input/output names |
| `{model_prefix}model.onnx` | The fitted pipeline |
| `{model_prefix}split.json` | `{"train_index", "test_index"}`: 0-based row positions in the dataset CSV |
| `dataset_key` | The CSV the model was trained on |

`load_context` (`context.py`) reads all four, selects feature and target columns by `metadata.feature_columns` /
`target_column` / `id_column`, and selects train/test rows by the split's positions (`df.iloc[...]`, not by index
label). For classification, the target is label-encoded to the class index in `metadata.classes` (compared as
strings when the classes are strings); a value that isn't one of `metadata.classes` raises `InvalidDataError`, acked
and reported as a `failed` event rather than dead-lettered. Regression keeps the target numeric.

## The ONNX model adapter

`model.py` wraps the ONNX file as a scikit-learn-compatible estimator, so tools written against the scikit-learn
estimator interface — `sklearn.inspection.permutation_importance`, SHAP, LIME — can call it directly instead of
needing their own ONNX code:

- `OnnxClassifier` (`ClassifierMixin, BaseEstimator`, in that order so scikit-learn ≥ 1.6's tag-based estimator-type
  detection picks it up) and `OnnxRegressor` (`RegressorMixin, BaseEstimator`).
- `load_model(onnx_bytes, metadata)` picks the right one from `metadata.problem_type` and wires up the ONNX
  input/output tensor names from `metadata.onnx_input_name` / `onnx_outputs`.
- `fit` is a no-op returning `self` (the model is already trained) and `__sklearn_is_fitted__` always returns `True`,
  so scikit-learn tools that check fitted state before calling `predict` work unchanged.
- `predict` / `predict_proba` cast input to `float32` with `np.asarray(X, dtype=np.float32)` (a `DataFrame` keeps its
  column order) and run the cached `onnxruntime.InferenceSession`.
- The session is created lazily, on first use, and cached outside `__init__`'s parameters (on a private attribute,
  not a constructor argument), because `InferenceSession` objects can't be pickled or deep-copied and `sklearn.clone`
  rebuilds an estimator from `get_params()`, which only sees constructor parameters.
- `OnnxClassifier.classes_` is always `np.arange(n_classes)`: the ONNX label output is the label-encoded class index,
  not the original value, so tools that index into `predict_proba`'s columns by `classes_` get the right column.

## The step framework

`steps.py` defines `EvaluationStep`, a `Protocol` with `name`, `weight` (its share of the progress bar), `enabled(options)`
and `run(ctx)`, and `STEPS`, the single ordered list every evaluation job runs: **metrics, plots, global feature
importance, local feature importance, ensemble, fuzzy**. Keeping the order in one list, rather than scattered
registrations, makes the run order obvious at a glance; issues #29–#33 each append their step at the right position.
This issue implements only `metrics`, so `STEPS` currently holds just `MetricsStep`.

`run_steps(ctx, progress)` runs every step whose `enabled(ctx.job.options)` is `True`, in list order, and calls
`progress(fraction, step.name)` after each one, with `fraction` the cumulative weight of finished steps over the
total weight of enabled steps.

Each step reads and writes the shared `EvaluationContext` (`context.py`): the job, `ModelMetadata`, the wrapped
model, `x_train`/`y_train`/`x_test`/`y_test`, the `Storage`, `n_jobs`, `plot_options`, the `artifacts` list
(`{"key", "kind", "title"}`, appended to as each output is written) and a `results` dict steps use to pass data to
later steps (for example a global feature importance step's output feeding fuzzy interpretability). Three helpers
write an output, upload it and record the artifact in one call: `save_plot` (PNG via `helix_core.plotting.save_figure`,
under `plots/`), `save_table` (CSV with its index, under `tables/`) and `save_json` (at the job's `output_prefix`,
not under a subdirectory).

## Metrics

The only step enabled in this issue. Always runs (`enabled` returns `True` unconditionally) and computes the same
metrics for both the training and the test rows, writing `{model_prefix}`-relative `metrics.json` as
`{"train": {...}, "test": {...}}` and setting `ctx.results["metrics"]` to the same value. Ported and extended from
the original Helix's `helix/services/metrics.py` and `options/choices/metrics.py`.

| Problem type | Metrics |
| --- | --- |
| Classification | `accuracy`, `f1_weighted`, `precision_weighted`, `recall_weighted` (all with `zero_division=0`), `mcc`, `roc_auc` |
| Regression | `r2`, `mae`, `rmse` |

`roc_auc` is `roc_auc_score(y, proba[:, 1])` for binary classification, and
`roc_auc_score(y, proba, multi_class="ovr", average="weighted")` for more than two classes; it's `null` when the
split's `y` has only one class (the score is undefined).

## Outputs

Under the job's `output_prefix` ([architecture.md](architecture.md#storage-layout-s3)): `metrics.json`,
`plots/{name}.png` and `tables/{name}.csv` as later steps add them. The `succeeded` event's `result` is
`{"metrics": ctx.results["metrics"]["test"], "artifacts": ctx.artifacts}` — the flat test-set metrics (what the API
shows first) plus every artifact written, train metrics included only inside `metrics.json` itself.
