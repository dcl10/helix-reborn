"""The evaluation step framework.

An evaluation job runs a fixed, ordered list of steps (:data:`STEPS`). Each step
decides for itself whether it applies to a job's options
(:meth:`EvaluationStep.enabled`) and, when it does, reads and writes the shared
:class:`~helix_evaluation.context.EvaluationContext`. Keeping the list in one place
(rather than scattered registrations) makes the order plots, global/local feature
importance, ensembling and fuzzy interpretability run in obvious at a glance.
"""

import logging
from collections.abc import Callable
from typing import Protocol

import numpy as np
import pandas as pd
import shap

from helix_core.messages import EvaluationOptions
from helix_evaluation.context import EvaluationContext
from helix_evaluation.fi.ensemble import EnsembleStep
from helix_evaluation.fi.fuzzy import FuzzyStep
from helix_evaluation.fi.global_methods import (
    normalise,
    permutation_importance_df,
    shap_global_df,
)
from helix_evaluation.fi.local_methods import lime_values, local_shap_values
from helix_evaluation.fi.plots import (
    plot_global_shap_importance,
    plot_local_importance_violin,
    plot_permutation_importance,
    plot_shap_beeswarm,
    plot_shap_waterfall,
)
from helix_evaluation.metrics import MetricsStep
from helix_evaluation.plots import (
    plot_confusion_matrix,
    plot_precision_recall,
    plot_predicted_vs_actual,
    plot_residuals,
    plot_residuals_histogram,
    plot_roc,
)

logger = logging.getLogger(__name__)

Progress = Callable[[float, str], None]
"""Reports evaluation progress: ``progress(fraction, message)``."""

_MAX_EXPLAIN_ROWS = 500
_SHAP_BACKGROUND_ROWS = 100
_PERMUTATION_SCORING = {
    "classification": "f1_weighted",
    "regression": "neg_mean_absolute_error",
}


class EvaluationStep(Protocol):
    """One stage of an evaluation job.

    Attributes:
        name: The step's name, used as a progress message and results key.
        weight: The step's share of the overall progress bar (0-1); weights across
            :data:`STEPS` should sum to roughly 1.
    """

    name: str
    weight: float

    def enabled(self, options: EvaluationOptions) -> bool:
        """Decide whether this step runs for a job's options.

        Args:
            options: The evaluation job's options.

        Returns:
            ``True`` if the step should run.
        """
        ...

    def run(self, ctx: EvaluationContext) -> None:
        """Run the step, reading and writing ``ctx`` in place.

        Args:
            ctx: The evaluation context to read from and write results, artifacts
                and plots/tables/JSON into.
        """
        ...


class PlotsStep:
    """Renders standard performance plots for the test set.

    Ported from the original Helix's ``helix/services/plotting.py``. Classification
    jobs get a confusion matrix (counts and row-normalised), an ROC curve and a
    precision-recall curve; regression jobs get predicted-vs-actual, residuals and a
    residual histogram.
    """

    name = "plots"
    weight = 0.1

    def enabled(self, options: EvaluationOptions) -> bool:
        """Return ``True`` when ``options.plots`` is set.

        Args:
            options: The evaluation job's options.

        Returns:
            ``options.plots``.
        """
        return options.plots

    def run(self, ctx: EvaluationContext) -> None:
        """Render and save the test-set plots for the job's problem type.

        Args:
            ctx: The evaluation context to read the model, test data and plot
                options from, and to save plots into.
        """
        opts = ctx.plot_options
        y_pred = ctx.model.predict(ctx.x_test)

        if ctx.metadata.problem_type == "classification":
            labels = [str(label) for label in ctx.metadata.classes or []]
            proba = ctx.model.predict_proba(ctx.x_test)

            ctx.save_plot(
                "confusion_matrix",
                plot_confusion_matrix(ctx.y_test, y_pred, labels, False, opts),
                "Confusion matrix",
            )
            ctx.save_plot(
                "confusion_matrix_normalised",
                plot_confusion_matrix(ctx.y_test, y_pred, labels, True, opts),
                "Confusion matrix (normalised)",
            )

            if len(np.unique(ctx.y_test)) < 2:
                logger.warning(
                    "Skipping ROC and precision-recall curves: the test set has "
                    "only one class"
                )
            else:
                ctx.save_plot(
                    "roc_curve",
                    plot_roc(ctx.y_test, proba, labels, opts),
                    "ROC curve",
                )
                ctx.save_plot(
                    "precision_recall_curve",
                    plot_precision_recall(ctx.y_test, proba, labels, opts),
                    "Precision-recall curve",
                )
        else:
            ctx.save_plot(
                "predicted_vs_actual",
                plot_predicted_vs_actual(ctx.y_test, y_pred, opts),
                "Predicted vs actual",
            )
            ctx.save_plot(
                "residuals",
                plot_residuals(ctx.y_test, y_pred, opts),
                "Residuals",
            )
            ctx.save_plot(
                "residuals_histogram",
                plot_residuals_histogram(ctx.y_test, y_pred, opts),
                "Residual distribution",
            )


class GlobalImportanceStep:
    """Computes global feature importance: permutation importance and SHAP.

    Ported from the original Helix's ``global_methods.py`` and
    ``FeatureImportanceEstimator._global_feature_importance``, run once on the
    ONNX model adapter rather than per fold.
    """

    name = "global_importance"
    weight = 0.3

    def enabled(self, options: EvaluationOptions) -> bool:
        """Return ``True`` when at least one global method is requested.

        Args:
            options: The evaluation job's options.

        Returns:
            ``True`` if ``options.global_methods`` is non-empty.
        """
        return bool(options.global_methods)

    def run(self, ctx: EvaluationContext) -> None:
        """Run the enabled global methods and store their normalised results.

        Explains on the test set, sampled to :data:`_MAX_EXPLAIN_ROWS` rows when
        larger (``random_state = metadata.pipeline.split.random_seed``). The SHAP
        background is ``shap.sample(x_train, 100, random_state=seed)``. Writes
        ``global_permutation`` and/or ``global_shap`` tables and plots, and sets
        ``ctx.results["global_importance"]`` to a dataframe of features x methods,
        each column min-max normalised to ``[0, 1]``, including only the methods
        that ran.

        Args:
            ctx: The evaluation context to read the model and data from, and to
                write tables, plots and the ``global_importance`` result into.
        """
        options = ctx.job.options
        seed = ctx.metadata.pipeline.split.random_seed
        problem_type = ctx.metadata.problem_type

        x_explain = ctx.x_test
        y_explain = ctx.y_test
        if len(x_explain) > _MAX_EXPLAIN_ROWS:
            x_explain = x_explain.sample(n=_MAX_EXPLAIN_ROWS, random_state=seed)
            y_explain = y_explain[x_explain.index.to_numpy()]

        columns: dict[str, pd.Series] = {}

        if "permutation" in options.global_methods:
            perm_df = permutation_importance_df(
                ctx.model,
                x_explain,
                y_explain,
                scoring=_PERMUTATION_SCORING[problem_type],
                n_repeats=options.permutation_repeats,
                random_state=seed,
                n_jobs=ctx.n_jobs,
            )
            ctx.save_table("global_permutation", perm_df, "Permutation importance")
            ctx.save_plot(
                "global_permutation",
                plot_permutation_importance(
                    perm_df,
                    ctx.plot_options,
                    options.num_features_to_plot,
                    "Permutation importance",
                ),
                "Permutation importance",
            )
            columns["Permutation"] = normalise(perm_df["importance_mean"])

        if "shap" in options.global_methods:
            x_background = shap.sample(
                ctx.x_train, _SHAP_BACKGROUND_ROWS, random_state=seed
            )
            shap_df, explanation = shap_global_df(ctx.model, x_background, x_explain)
            ctx.save_table("global_shap", shap_df, "SHAP importance (mean |SHAP|)")
            ctx.save_plot(
                "global_shap_bar",
                plot_global_shap_importance(
                    shap_df,
                    ctx.plot_options,
                    options.num_features_to_plot,
                    "SHAP importance (mean |SHAP|)",
                ),
                "SHAP importance (mean |SHAP|)",
            )
            ctx.save_plot(
                "global_shap_beeswarm",
                plot_shap_beeswarm(
                    explanation,
                    ctx.plot_options,
                    options.num_features_to_plot,
                    "SHAP importance (beeswarm)",
                ),
                "SHAP importance (beeswarm)",
            )
            columns["SHAP"] = normalise(shap_df["mean_abs_shap"])

        ctx.results["global_importance"] = pd.DataFrame(columns)


class LocalImportanceStep:
    """Computes local feature importance: LIME and per-sample SHAP values.

    Ported from the original Helix's ``local_methods.py`` and
    ``FeatureImportanceEstimator._local_feature_importance``, run once on the ONNX
    model adapter rather than per fold.
    """

    name = "local_importance"
    weight = 0.25

    def enabled(self, options: EvaluationOptions) -> bool:
        """Return ``True`` when at least one local method is requested.

        Args:
            options: The evaluation job's options.

        Returns:
            ``True`` if ``options.local_methods`` is non-empty.
        """
        return bool(options.local_methods)

    def run(self, ctx: EvaluationContext) -> None:
        """Run the enabled local methods and store their per-sample results.

        Explains the first ``min(options.local_max_samples, len(x_test))`` test
        rows after a seeded shuffle (``random_state =
        metadata.pipeline.split.random_seed``). The SHAP background is
        ``shap.sample(x_train, 100, random_state=seed)``. Writes ``local_lime``
        and/or ``local_shap`` tables and plots, and sets
        ``ctx.results["local_importance"]`` to ``{"LIME": df, "SHAP": df}``,
        including only the methods that ran.

        Args:
            ctx: The evaluation context to read the model and data from, and to
                write tables, plots and the ``local_importance`` result into.
        """
        options = ctx.job.options
        seed = ctx.metadata.pipeline.split.random_seed
        n_plot = options.num_features_to_plot

        n_samples = min(options.local_max_samples, len(ctx.x_test))
        x_explain = ctx.x_test.sample(frac=1, random_state=seed).head(n_samples)

        results: dict[str, pd.DataFrame] = {}

        if "lime" in options.local_methods:
            lime_df = lime_values(
                ctx.model,
                ctx.x_train,
                x_explain,
                ctx.metadata.problem_type,
                random_state=seed,
            )
            ctx.save_table("local_lime", lime_df, "LIME values")
            title = "LIME: most important features"
            ctx.save_plot(
                "local_lime_violin",
                plot_local_importance_violin(lime_df, ctx.plot_options, n_plot, title),
                title,
            )
            results["LIME"] = lime_df

        if "shap" in options.local_methods:
            x_background = shap.sample(
                ctx.x_train, _SHAP_BACKGROUND_ROWS, random_state=seed
            )
            shap_df, explanation = local_shap_values(ctx.model, x_background, x_explain)
            ctx.save_table("local_shap", shap_df, "Local SHAP values")
            title = "Local SHAP: most important features"
            ctx.save_plot(
                "local_shap_violin",
                plot_local_importance_violin(shap_df, ctx.plot_options, n_plot, title),
                title,
            )
            title = "SHAP waterfall: first explained sample"
            ctx.save_plot(
                "local_shap_waterfall_0",
                plot_shap_waterfall(explanation[0], ctx.plot_options, n_plot, title),
                title,
            )
            results["SHAP"] = shap_df

        ctx.results["local_importance"] = results


STEPS: list[EvaluationStep] = [
    MetricsStep(),
    PlotsStep(),
    GlobalImportanceStep(),
    LocalImportanceStep(),
    EnsembleStep(),
    FuzzyStep(),
]
"""Every evaluation step, in run order: metrics, plots, global FI, local FI, ensemble,
fuzzy."""


def run_steps(ctx: EvaluationContext, progress: Progress) -> None:
    """Run every enabled step in :data:`STEPS`, in order, reporting progress.

    Args:
        ctx: The evaluation context the steps read and write.
        progress: Called with an increasing fraction and the step's name as each
            enabled step finishes.
    """
    enabled_steps = [step for step in STEPS if step.enabled(ctx.job.options)]
    total_weight = sum(step.weight for step in enabled_steps) or 1.0
    completed_weight = 0.0
    for step in enabled_steps:
        step.run(ctx)
        completed_weight += step.weight
        progress(completed_weight / total_weight, step.name)
