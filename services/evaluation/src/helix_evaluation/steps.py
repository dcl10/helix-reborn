"""The evaluation step framework.

An evaluation job runs a fixed, ordered list of steps (:data:`STEPS`). Each step
decides for itself whether it applies to a job's options
(:meth:`EvaluationStep.enabled`) and, when it does, reads and writes the shared
:class:`~helix_evaluation.context.EvaluationContext`. Keeping the list in one place
(rather than scattered registrations) makes the order plots, global/local feature
importance, ensembling and fuzzy interpretability run in obvious at a glance; later
issues (#29-#33) insert their step at the right position.
"""

import logging
from collections.abc import Callable
from typing import Protocol

import numpy as np

from helix_core.messages import EvaluationOptions
from helix_evaluation.context import EvaluationContext
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


STEPS: list[EvaluationStep] = [MetricsStep(), PlotsStep()]
"""Every evaluation step, in run order: metrics, plots, global FI, local FI, ensemble,
fuzzy. #30-#33 append the rest."""


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
