"""The metrics step: test-set and train-set performance metrics.

Ported and extended from the original Helix's ``helix/services/metrics.py`` and
``helix/options/choices/metrics.py``. Always enabled; later steps (plots, feature
importance) build on its output but it needs nothing from them.
"""

from typing import Any

import numpy as np
from sklearn.metrics import (
    accuracy_score,
    f1_score,
    matthews_corrcoef,
    mean_absolute_error,
    precision_score,
    r2_score,
    recall_score,
    roc_auc_score,
    root_mean_squared_error,
)

from helix_core.messages import EvaluationOptions
from helix_evaluation.context import EvaluationContext


def _classification_metrics(
    y_true: np.ndarray, y_pred: np.ndarray, proba: np.ndarray
) -> dict[str, float | None]:
    """Compute classification metrics for one split.

    Args:
        y_true: The true, label-encoded class indices.
        y_pred: The predicted class indices.
        proba: The predicted class probabilities, shape ``[n, k]``.

    Returns:
        ``accuracy``, ``f1_weighted``, ``precision_weighted``, ``recall_weighted``,
        ``mcc`` and ``roc_auc`` (``None`` when ``y_true`` has only one class).
    """
    metrics: dict[str, float | None] = {
        "accuracy": float(accuracy_score(y_true, y_pred)),
        "f1_weighted": float(
            f1_score(y_true, y_pred, average="weighted", zero_division=0)
        ),
        "precision_weighted": float(
            precision_score(y_true, y_pred, average="weighted", zero_division=0)
        ),
        "recall_weighted": float(
            recall_score(y_true, y_pred, average="weighted", zero_division=0)
        ),
        "mcc": float(matthews_corrcoef(y_true, y_pred)),
    }
    if len(np.unique(y_true)) < 2:
        metrics["roc_auc"] = None
    elif proba.shape[1] == 2:
        metrics["roc_auc"] = float(roc_auc_score(y_true, proba[:, 1]))
    else:
        metrics["roc_auc"] = float(
            roc_auc_score(y_true, proba, multi_class="ovr", average="weighted")
        )
    return metrics


def _regression_metrics(y_true: np.ndarray, y_pred: np.ndarray) -> dict[str, float]:
    """Compute regression metrics for one split.

    Args:
        y_true: The true target values.
        y_pred: The predicted target values.

    Returns:
        ``r2``, ``mae`` and ``rmse``.
    """
    return {
        "r2": float(r2_score(y_true, y_pred)),
        "mae": float(mean_absolute_error(y_true, y_pred)),
        "rmse": float(root_mean_squared_error(y_true, y_pred)),
    }


def _split_metrics(
    ctx: EvaluationContext, x: Any, y: np.ndarray
) -> dict[str, float | None]:
    y_pred = ctx.model.predict(x)
    if ctx.metadata.problem_type == "classification":
        proba = ctx.model.predict_proba(x)
        return _classification_metrics(y, y_pred, proba)
    return _regression_metrics(y, y_pred)


class MetricsStep:
    """Computes train-set and test-set metrics and writes ``metrics.json``."""

    name = "metrics"
    weight = 1.0

    def enabled(self, options: EvaluationOptions) -> bool:
        """Return ``True``: metrics are always computed.

        Args:
            options: The evaluation job's options.

        Returns:
            ``True``.
        """
        return True

    def run(self, ctx: EvaluationContext) -> None:
        """Compute train and test metrics and store them in ``ctx``.

        Writes ``metrics.json`` as ``{"train": {...}, "test": {...}}`` and sets
        ``ctx.results["metrics"]`` to the same value.

        Args:
            ctx: The evaluation context to read the model and data from, and to
                write the metrics artifact and result into.
        """
        metrics = {
            "train": _split_metrics(ctx, ctx.x_train, ctx.y_train),
            "test": _split_metrics(ctx, ctx.x_test, ctx.y_test),
        }
        ctx.save_json("metrics", metrics, "Metrics")
        ctx.results["metrics"] = metrics
