"""Standard plots: confusion matrix, ROC/PR curves, predicted vs actual, residuals.

Ported from the original Helix's ``helix/services/plotting.py`` (``plot_auc_roc``,
``plot_confusion_matrix`` and ``plot_scatter``), dropping the Streamlit code: each
function here is pure, returning a :class:`~matplotlib.figure.Figure` for
:meth:`~helix_evaluation.context.EvaluationContext.save_plot` to render and upload.
"""

import numpy as np
import seaborn as sns
from matplotlib import pyplot as plt
from matplotlib.figure import Figure
from sklearn.metrics import (
    auc,
    average_precision_score,
    confusion_matrix,
    precision_recall_curve,
    r2_score,
    roc_curve,
)
from sklearn.preprocessing import label_binarize

from helix_core.messages import PlotOptions
from helix_core.plotting import apply_style, finish_axes


def plot_confusion_matrix(
    y_true: np.ndarray,
    y_pred: np.ndarray,
    labels: list[str],
    normalise: bool,
    opts: PlotOptions,
) -> Figure:
    """Plot a confusion matrix as a counts (or row-normalised) heatmap.

    Args:
        y_true: The true, label-encoded class indices.
        y_pred: The predicted class indices.
        labels: The class labels, as strings, in index order. Used as both axes'
            tick labels.
        normalise: If ``True``, normalise each row (true class) to sum to 1.
        opts: Styling options; ``opts.colour_map`` sets the heatmap's colour map.

    Returns:
        The rendered figure.
    """
    apply_style(opts)
    cm = confusion_matrix(
        y_true,
        y_pred,
        labels=range(len(labels)),
        normalize="true" if normalise else None,
    )
    fig, ax = plt.subplots(figsize=(opts.width, opts.height))
    sns.heatmap(
        cm,
        annot=True,
        fmt=".2f" if normalise else "d",
        cmap=opts.colour_map,
        xticklabels=labels,
        yticklabels=labels,
        ax=ax,
    )
    title = "Confusion matrix (normalised)" if normalise else "Confusion matrix"
    finish_axes(ax, opts, title, "Predicted label", "True label")
    fig.tight_layout()
    return fig


def plot_roc(
    y_true: np.ndarray, proba: np.ndarray, labels: list[str], opts: PlotOptions
) -> Figure:
    """Plot ROC curve(s) for a classifier's test-set predictions.

    For a binary problem, plots a single curve (for the positive class) with its AUC
    in the legend. For more than two classes, plots a one-vs-rest curve per class
    plus the micro-average, each with its AUC in the legend.

    Args:
        y_true: The true, label-encoded class indices.
        proba: The predicted class probabilities, shape ``[n, k]``.
        labels: The class labels, as strings, in index order.
        opts: Styling options.

    Returns:
        The rendered figure.
    """
    apply_style(opts)
    fig, ax = plt.subplots(figsize=(opts.width, opts.height))
    n_classes = proba.shape[1]

    if n_classes == 2:
        fpr, tpr, _ = roc_curve(y_true, proba[:, 1])
        score = auc(fpr, tpr)
        ax.plot(fpr, tpr, label=f"AUC = {score:.3f}")
    else:
        y_bin = label_binarize(y_true, classes=range(n_classes))
        for i, label in enumerate(labels):
            fpr, tpr, _ = roc_curve(y_bin[:, i], proba[:, i])
            score = auc(fpr, tpr)
            ax.plot(fpr, tpr, label=f"{label} (AUC = {score:.3f})")
        fpr_micro, tpr_micro, _ = roc_curve(y_bin.ravel(), proba.ravel())
        score_micro = auc(fpr_micro, tpr_micro)
        ax.plot(
            fpr_micro,
            tpr_micro,
            linestyle="--",
            label=f"micro-average (AUC = {score_micro:.3f})",
        )

    ax.plot([0, 1], [0, 1], linestyle=":", color="grey", label="Chance")
    finish_axes(ax, opts, "ROC curve", "False positive rate", "True positive rate")
    ax.legend(loc="lower right", fontsize=opts.tick_size)
    fig.tight_layout()
    return fig


def plot_precision_recall(
    y_true: np.ndarray, proba: np.ndarray, labels: list[str], opts: PlotOptions
) -> Figure:
    """Plot precision-recall curve(s) for a classifier's test-set predictions.

    For a binary problem, plots a single curve (for the positive class) with its
    average precision in the legend. For more than two classes, plots a one-vs-rest
    curve per class plus the micro-average, each with its average precision in the
    legend.

    Args:
        y_true: The true, label-encoded class indices.
        proba: The predicted class probabilities, shape ``[n, k]``.
        labels: The class labels, as strings, in index order.
        opts: Styling options.

    Returns:
        The rendered figure.
    """
    apply_style(opts)
    fig, ax = plt.subplots(figsize=(opts.width, opts.height))
    n_classes = proba.shape[1]

    if n_classes == 2:
        precision, recall, _ = precision_recall_curve(y_true, proba[:, 1])
        score = average_precision_score(y_true, proba[:, 1])
        ax.plot(recall, precision, label=f"AP = {score:.3f}")
    else:
        y_bin = label_binarize(y_true, classes=range(n_classes))
        for i, label in enumerate(labels):
            precision, recall, _ = precision_recall_curve(y_bin[:, i], proba[:, i])
            score = average_precision_score(y_bin[:, i], proba[:, i])
            ax.plot(recall, precision, label=f"{label} (AP = {score:.3f})")
        precision_micro, recall_micro, _ = precision_recall_curve(
            y_bin.ravel(), proba.ravel()
        )
        score_micro = average_precision_score(y_bin, proba, average="micro")
        ax.plot(
            recall_micro,
            precision_micro,
            linestyle="--",
            label=f"micro-average (AP = {score_micro:.3f})",
        )

    finish_axes(ax, opts, "Precision-recall curve", "Recall", "Precision")
    ax.legend(loc="lower left", fontsize=opts.tick_size)
    fig.tight_layout()
    return fig


def plot_predicted_vs_actual(
    y_true: np.ndarray, y_pred: np.ndarray, opts: PlotOptions
) -> Figure:
    """Plot a predicted-vs-actual scatter plot, with a y=x reference line.

    Ported from the original Helix's ``plot_scatter``.

    Args:
        y_true: The true target values.
        y_pred: The predicted target values.
        opts: Styling options.

    Returns:
        The rendered figure, titled with the R² between ``y_true`` and ``y_pred``.
    """
    apply_style(opts)
    fig, ax = plt.subplots(figsize=(opts.width, opts.height))
    sns.scatterplot(x=y_true, y=y_pred, ax=ax)
    lo = min(y_true.min(), y_pred.min())
    hi = max(y_true.max(), y_pred.max())
    ax.plot([lo, hi], [lo, hi], "k--", lw=2, label="y = x")
    r2 = r2_score(y_true, y_pred)
    title = f"Predicted vs actual (R² = {r2:.3f})"
    finish_axes(ax, opts, title, "Actual", "Predicted")
    ax.legend(loc="upper left", fontsize=opts.tick_size)
    fig.tight_layout()
    return fig


def plot_residuals(y_true: np.ndarray, y_pred: np.ndarray, opts: PlotOptions) -> Figure:
    """Plot residuals (actual minus predicted) against predicted values.

    Args:
        y_true: The true target values.
        y_pred: The predicted target values.
        opts: Styling options.

    Returns:
        The rendered figure, with a y=0 reference line.
    """
    apply_style(opts)
    residuals = y_true - y_pred
    fig, ax = plt.subplots(figsize=(opts.width, opts.height))
    sns.scatterplot(x=y_pred, y=residuals, ax=ax)
    ax.axhline(0, color="k", linestyle="--", lw=2)
    finish_axes(ax, opts, "Residuals", "Predicted", "Residual (actual - predicted)")
    fig.tight_layout()
    return fig


def plot_residuals_histogram(
    y_true: np.ndarray, y_pred: np.ndarray, opts: PlotOptions
) -> Figure:
    """Plot a histogram, with a KDE overlay, of the residuals.

    Args:
        y_true: The true target values.
        y_pred: The predicted target values.
        opts: Styling options.

    Returns:
        The rendered figure.
    """
    apply_style(opts)
    residuals = y_true - y_pred
    fig, ax = plt.subplots(figsize=(opts.width, opts.height))
    sns.histplot(residuals, kde=True, ax=ax)
    finish_axes(
        ax, opts, "Residual distribution", "Residual (actual - predicted)", "Count"
    )
    fig.tight_layout()
    return fig
