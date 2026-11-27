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
registrations, makes the run order obvious at a glance; all six are implemented.

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

## Local feature importance

`LocalImportanceStep` (`fi/local_methods.py`, `fi/plots.py`), ported from the original Helix's
`services/feature_importance/local_methods.py` and the violin plot from `services/plotting.py`
(`plot_most_important_feats_violin`). Enabled when `options.local_methods` is non-empty; it lists any of `"lime"`,
`"shap"`. Where global importance gives one number per feature, local importance explains individual predictions: one
value per feature **per test row**.

It explains the first `min(options.local_max_samples, len(x_test))` test rows after a shuffle seeded with
`metadata.pipeline.split.random_seed`. LIME is slow (it fits a surrogate model per row), which is why the sample is
capped.

- **LIME** (`lime_values`): `LimeTabularExplainer(x_train, mode=problem_type, discretize_continuous=True,
  random_state=seed)`. Classification explains `predict_proba` — the positive class (label 1) for binary problems, the
  top predicted label for multi-class; regression explains `predict`. Each explanation's `(feature_index, weight)`
  pairs from `as_map()` are mapped back to their column, so values line up with features. (The original Helix read
  `local_exp[1]`, which is ordered by importance rather than by feature, and assumed label 1.) LIME's warnings are
  silenced inside the call only. Writes table `local_lime` and plot `local_lime_violin`.
- **SHAP** (`local_shap_values`): `shap.Explainer(ctx.model.predict, shap.sample(x_train, 100, random_state=seed))`,
  as for global SHAP. Writes table `local_shap`, plot `local_shap_violin` and plot `local_shap_waterfall_0` (the
  first explained row's waterfall, `num_features_to_plot` bars).

The violin plots show the distribution of each feature's local values for the top `options.num_features_to_plot`
features by mean |value|. Tables have one row per explained sample (indexed by its position in the test set) and
one column per feature, in the model's feature order.

`ctx.results["local_importance"]` is `{"LIME": df, "SHAP": df}` with only the methods that ran — the input the fuzzy
step (#33) reads.

## Ensemble feature importance

`EnsembleStep` (`fi/ensemble.py`), ported from the original Helix's `services/feature_importance/ensemble_methods.py`
(`calculate_ensemble_mean`, `calculate_ensemble_majorityvote`) with the logic unchanged, and the bar chart from
`plot_bar_chart`. Enabled when `options.ensemble_methods` is non-empty; it lists any of `"mean"`, `"majority_vote"`.
It combines the global methods into one importance per feature, so it needs at least one global method: without
`ctx.results["global_importance"]` it raises `InvalidSpecError("Ensemble importance needs at least one global
method")`.

The original Helix ensembled across several models and folds. An evaluation covers one model, so both functions get a
single-entry dict, `{model step: ctx.results["global_importance"]}` (rows = features, columns = methods, values
normalised to `[0, 1]`).

- **Mean** (`ensemble_mean`): each feature's average across methods. Writes table and plot `ensemble_mean`.
- **Majority vote** (`ensemble_majority_vote`): each method ranks the features (dense, 1 = most important); a
  feature's importance is the mean of its values over all methods, counting as 0 the values from methods that didn't
  give it its most common rank. When ranks tie for most common, the first two count and any further tied ranks are
  dropped, as in the original. With only two methods every rank is a tie or a match, so the result equals the mean.
  Writes table and plot `ensemble_majority_vote`.

Both also return a standard deviation over repeated rows for the same feature (from the original's multi-fold
input). With one model that's always `NaN`, so the bar charts have no error bars.

`ctx.results["ensemble_importance"]` is `{"Mean": df, "Majority Vote": df}` with only the methods that ran (the keys
the fuzzy step, #33, expects); each frame has one column, `"Mean Importance"` or `"Majority Vote Importance"`, sorted
descending.

## Fuzzy interpretation

`FuzzyStep` (`fi/fuzzy.py`), ported from the original Helix's `feature_importance/fuzzy.py` (`Fuzzy`, here
`FuzzyInterpreter`) and the plotting helpers `save_fuzzy_sets_plots` / `save_target_clusters_plots`. Enabled when
`options.fuzzy` is set; `EvaluationOptions` validation guarantees at least one local and one ensemble method ran, and
it reads `ctx.results["local_importance"]` and `ctx.results["ensemble_importance"]`.

**What it tells you.** Local importance gives a number per feature per sample; fuzzy interpretation turns those into
rules like "when the target is *high*, x0 has *high* importance and x3 has *low* importance". Read the outputs as:

- A **context** is a group of samples: the target class for classification, or for regression a fuzzy cluster of the
  target values named by `cluster_names`, lowest cluster first ("very low" … "very high").
- Each sample's importance for a feature is labelled **low**, **medium** or **high** relative to all explained samples
  (importances are min-max scaled to `[0, 1]` per method, then matched against fixed membership functions: Z-shaped
  for low, a triangle around 0.5 for medium, S-shaped for high). Scaling is of the signed value, so "low" means "the
  most negative contribution seen", not "close to zero".
- A **rule** is one sample's combination of labels within its context. **`fuzzy_rules`** ("Fuzzy rules") lists the
  `number_rules` most common rules per context, with `records`, how many samples (across LIME and SHAP) follow each.
- **`fuzzy_contextual_rules`** ("Fuzzy rules by context") summarises them: rows are features, columns are contexts,
  and each cell is the highest label that feature has in any of the context's top rules. A feature that's "high" in
  the "very high" column and "low" elsewhere matters mostly for the largest target values.

**Steps.**

1. Pick the top `number_fuzzy_features` features by the majority-vote ensemble, falling back to the mean ensemble
   (with a log warning) when majority vote wasn't run. Only these features appear in the rules.
2. If `granular_features`, build low/medium/high membership functions for each selected feature from the training
   set's quartiles and plot them: `fuzzy_sets_{feature}` (the name sanitised to `[A-Za-z0-9_-]`).
3. Scale each local method's importances to `[0, 1]`, append the explained samples' targets (class labels for
   classification) and stack the methods.
4. Regression only: cluster the target with fuzzy c-means (`number_clusters`, seeded with the split's random seed),
   name clusters by their mean target and plot `fuzzy_target_clusters` (box plots of the target per cluster).
5. Label every importance low/medium/high, count rule occurrences per context and write both tables.

**Differences from the original**, each needed for the options to have their documented effect: feature selection
sorted by a column `0` that doesn't exist (so it always failed silently) and the rules then used every feature; the
membership plots were drawn for train and then test into the same files; c-means wasn't seeded; and the first table
was the per-sample rules, then overwritten by the summary. Known quirks kept from the original: the cluster naming
pairs present cluster numbers with averages by position, so if c-means leaves a cluster empty, names can shift.

## Outputs

Under the job's `output_prefix` ([architecture.md](architecture.md#storage-layout-s3)): `metrics.json`,
`plots/{name}.png` and `tables/{name}.csv` as later steps add them. The `succeeded` event's `result` is
`{"metrics": ctx.results["metrics"]["test"], "artifacts": ctx.artifacts}` — the flat test-set metrics (what the API
shows first) plus every artifact written, train metrics included only inside `metrics.json` itself.
