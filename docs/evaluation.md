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
registrations, makes the run order obvious at a glance; issues #31–#33 each append their step at the right position.
`STEPS` currently holds `MetricsStep`, `PlotsStep` and `GlobalImportanceStep`.

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

## Plots

Enabled when `options.plots` is `True` (the default). Renders standard performance plots for the **test set only**,
using the shared styling helpers in `helix_core.plotting` (`apply_style`, `finish_axes`) and `ctx.plot_options`, and
saves each one through `ctx.save_plot(name, fig, title)` (`plots/{name}.png`, recorded as a `plot` artifact). Ported
from the original Helix's `helix/services/plotting.py` (`plot_auc_roc`, `plot_confusion_matrix`, `plot_scatter`),
dropping the Streamlit code. Tick labels for the confusion matrix and legend entries for the ROC/PR curves use the
encoded `y` with `metadata.classes` as labels, not classes re-derived from the data.

| Name | Problem type | Title | Content |
| --- | --- | --- | --- |
| `confusion_matrix` | classification | "Confusion matrix" | Counts heatmap, tick labels = `metadata.classes` (as strings), colour map `plot_options.colour_map` |
| `confusion_matrix_normalised` | classification | "Confusion matrix (normalised)" | Row-normalised |
| `roc_curve` | classification | "ROC curve" | Binary: one curve with the AUC in the legend. Multi-class: one-vs-rest curve per class plus the micro-average |
| `precision_recall_curve` | classification | "Precision-recall curve" | Same structure, with average precision in the legend |
| `predicted_vs_actual` | regression | "Predicted vs actual" | Scatter, y=x reference line, R² in the title |
| `residuals` | regression | "Residuals" | Residual (actual − predicted) vs predicted scatter, with a y=0 line |
| `residuals_histogram` | regression | "Residual distribution" | Histogram with KDE |

`roc_curve` and `precision_recall_curve` are skipped, with a log warning, when the test set's `y` has only one class
(the curves are undefined); the rest of the step still runs.

## Global feature importance

`GlobalImportanceStep` (`fi/global_methods.py`, `fi/plots.py`), ported from the original Helix's
`services/feature_importance/global_methods.py` and `FeatureImportanceEstimator._global_feature_importance`. Enabled
when `options.global_methods` (`EvaluationOptions`) is non-empty; it lists any of `"permutation"`, `"shap"`.

Both methods explain the same rows: the test set, sampled down to 500 rows with `random_state =
metadata.pipeline.split.random_seed` when it's larger. SHAP additionally needs a background sample, `shap.sample(
x_train, 100, random_state=seed)`.

- **Permutation importance** (`sklearn.inspection.permutation_importance`, `options.permutation_repeats` repeats,
  `ctx.n_jobs` workers): scored with `f1_weighted` for classification and `neg_mean_absolute_error` for regression
  (the original Helix's default, signed so higher is always better). Writes table `global_permutation`
  (`importance_mean`/`importance_std` per feature, sorted descending) and plot `global_permutation`, a horizontal bar
  chart of the top `options.num_features_to_plot` features with standard-deviation error bars.
- **SHAP**: `shap.Explainer(ctx.model.predict, x_background)` — a plain callable, so SHAP picks its model-agnostic
  Permutation explainer. Writes table `global_shap` (`mean_abs_shap` per feature, sorted descending), plot
  `global_shap_bar` (top-N bar chart by mean |SHAP|) and plot `global_shap_beeswarm`
  (`shap.plots.beeswarm`). For classification, `ctx.model.predict` returns the label-encoded class index rather than
  the original class value — the same behaviour as the original Helix, and acceptable because SHAP only needs a
  numeric model output to attribute.

`ctx.results["global_importance"]` is set to a `pandas.DataFrame` indexed by feature, with a `"Permutation"` and/or
`"SHAP"` column (only for the methods that ran) holding each method's importances min-max scaled to `[0, 1]` by
`normalise` (a constant series becomes all zeros). This is the shape the ensemble step (#32) averages across methods.

## Outputs

Under the job's `output_prefix` ([architecture.md](architecture.md#storage-layout-s3)): `metrics.json`,
`plots/{name}.png` and `tables/{name}.csv` as later steps add them. The `succeeded` event's `result` is
`{"metrics": ctx.results["metrics"]["test"], "artifacts": ctx.artifacts}` — the flat test-set metrics (what the API
shows first) plus every artifact written, train metrics included only inside `metrics.json` itself.
