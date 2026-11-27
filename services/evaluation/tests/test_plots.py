import logging

import numpy as np
import pytest
from matplotlib.figure import Figure

from helix_core.messages import EvaluationOptions, PlotOptions
from helix_evaluation.context import load_context
from helix_evaluation.plots import (
    plot_confusion_matrix,
    plot_precision_recall,
    plot_predicted_vs_actual,
    plot_residuals,
    plot_residuals_histogram,
    plot_roc,
)
from helix_evaluation.steps import PlotsStep, run_steps

BINARY_OPTS = PlotOptions()
BINARY_Y_TRUE = np.array([0, 0, 1, 1, 0, 1])
BINARY_Y_PRED = np.array([0, 1, 1, 1, 0, 0])
BINARY_PROBA = np.array(
    [[0.9, 0.1], [0.4, 0.6], [0.2, 0.8], [0.1, 0.9], [0.7, 0.3], [0.6, 0.4]]
)

MULTI_LABELS = ["a", "b", "c"]
MULTI_Y_TRUE = np.array([0, 1, 2, 1, 0, 2])
MULTI_Y_PRED = np.array([0, 1, 2, 0, 0, 2])
MULTI_PROBA = np.array(
    [
        [0.8, 0.1, 0.1],
        [0.1, 0.8, 0.1],
        [0.1, 0.1, 0.8],
        [0.5, 0.3, 0.2],
        [0.7, 0.2, 0.1],
        [0.1, 0.2, 0.7],
    ]
)

REG_Y_TRUE = np.array([1.0, 2.0, 3.0, 4.0, 5.0])
REG_Y_PRED = np.array([1.2, 1.8, 3.3, 3.7, 5.5])


def _close(fig: Figure) -> None:
    import matplotlib.pyplot as plt

    plt.close(fig)


def test_plot_confusion_matrix_binary() -> None:
    fig = plot_confusion_matrix(
        BINARY_Y_TRUE, BINARY_Y_PRED, ["no", "yes"], False, BINARY_OPTS
    )

    assert isinstance(fig, Figure)
    ax = fig.axes[0]
    assert ax.get_title() == "Confusion matrix"
    assert ax.get_xlabel() == "Predicted label"
    assert ax.get_ylabel() == "True label"
    _close(fig)


def test_plot_confusion_matrix_normalised_title() -> None:
    fig = plot_confusion_matrix(
        BINARY_Y_TRUE, BINARY_Y_PRED, ["no", "yes"], True, BINARY_OPTS
    )

    assert fig.axes[0].get_title() == "Confusion matrix (normalised)"
    _close(fig)


def test_plot_confusion_matrix_multiclass() -> None:
    fig = plot_confusion_matrix(
        MULTI_Y_TRUE, MULTI_Y_PRED, MULTI_LABELS, False, BINARY_OPTS
    )

    assert isinstance(fig, Figure)
    _close(fig)


def test_plot_roc_binary() -> None:
    fig = plot_roc(BINARY_Y_TRUE, BINARY_PROBA, ["no", "yes"], BINARY_OPTS)

    ax = fig.axes[0]
    assert ax.get_title() == "ROC curve"
    assert ax.get_xlabel() == "False positive rate"
    assert ax.get_ylabel() == "True positive rate"
    labels = [line.get_label() for line in ax.get_lines()]
    assert any("AUC" in label for label in labels)
    _close(fig)


def test_plot_roc_multiclass_has_one_curve_per_class_plus_micro() -> None:
    fig = plot_roc(MULTI_Y_TRUE, MULTI_PROBA, MULTI_LABELS, BINARY_OPTS)

    labels = [line.get_label() for line in fig.axes[0].get_lines()]
    for label in MULTI_LABELS:
        assert any(label in curve_label for curve_label in labels)
    assert any("micro-average" in curve_label for curve_label in labels)
    _close(fig)


def test_plot_roc_single_class_raises() -> None:
    y_true = np.zeros(4, dtype=np.int64)
    proba = np.array([[0.9, 0.1], [0.8, 0.2], [0.7, 0.3], [0.6, 0.4]])

    with pytest.warns(UserWarning):
        fig = plot_roc(y_true, proba, ["no", "yes"], BINARY_OPTS)
    _close(fig)


def test_plot_precision_recall_binary() -> None:
    fig = plot_precision_recall(BINARY_Y_TRUE, BINARY_PROBA, ["no", "yes"], BINARY_OPTS)

    ax = fig.axes[0]
    assert ax.get_title() == "Precision-recall curve"
    assert ax.get_xlabel() == "Recall"
    assert ax.get_ylabel() == "Precision"
    labels = [line.get_label() for line in ax.get_lines()]
    assert any("AP" in label for label in labels)
    _close(fig)


def test_plot_precision_recall_multiclass_has_one_curve_per_class_plus_micro() -> None:
    fig = plot_precision_recall(MULTI_Y_TRUE, MULTI_PROBA, MULTI_LABELS, BINARY_OPTS)

    labels = [line.get_label() for line in fig.axes[0].get_lines()]
    for label in MULTI_LABELS:
        assert any(label in curve_label for curve_label in labels)
    assert any("micro-average" in curve_label for curve_label in labels)
    _close(fig)


def test_plot_predicted_vs_actual() -> None:
    fig = plot_predicted_vs_actual(REG_Y_TRUE, REG_Y_PRED, BINARY_OPTS)

    ax = fig.axes[0]
    assert ax.get_title().startswith("Predicted vs actual (R")
    assert ax.get_xlabel() == "Actual"
    assert ax.get_ylabel() == "Predicted"
    _close(fig)


def test_plot_residuals() -> None:
    fig = plot_residuals(REG_Y_TRUE, REG_Y_PRED, BINARY_OPTS)

    ax = fig.axes[0]
    assert ax.get_title() == "Residuals"
    assert ax.get_xlabel() == "Predicted"
    assert ax.get_ylabel() == "Residual (actual - predicted)"
    _close(fig)


def test_plot_residuals_histogram() -> None:
    fig = plot_residuals_histogram(REG_Y_TRUE, REG_Y_PRED, BINARY_OPTS)

    ax = fig.axes[0]
    assert ax.get_title() == "Residual distribution"
    assert ax.get_xlabel() == "Residual (actual - predicted)"
    _close(fig)


def test_plots_step_writes_classification_plots(classification_env) -> None:
    storage, job, _df, _output = classification_env
    ctx = load_context(job, storage, n_jobs=1)

    PlotsStep().run(ctx)

    expected_keys = {
        "experiments/e/evaluations/v/plots/confusion_matrix.png",
        "experiments/e/evaluations/v/plots/confusion_matrix_normalised.png",
        "experiments/e/evaluations/v/plots/roc_curve.png",
        "experiments/e/evaluations/v/plots/precision_recall_curve.png",
    }
    written_keys = {artifact["key"] for artifact in ctx.artifacts}
    assert written_keys == expected_keys
    assert all(artifact["kind"] == "plot" for artifact in ctx.artifacts)
    for key in expected_keys:
        assert storage.get_bytes(key)


def test_plots_step_writes_regression_plots(regression_env) -> None:
    storage, job, _df, _output = regression_env
    ctx = load_context(job, storage, n_jobs=1)

    PlotsStep().run(ctx)

    expected_keys = {
        "experiments/e/evaluations/v/plots/predicted_vs_actual.png",
        "experiments/e/evaluations/v/plots/residuals.png",
        "experiments/e/evaluations/v/plots/residuals_histogram.png",
    }
    written_keys = {artifact["key"] for artifact in ctx.artifacts}
    assert written_keys == expected_keys
    assert all(artifact["kind"] == "plot" for artifact in ctx.artifacts)
    for key in expected_keys:
        assert storage.get_bytes(key)


def test_plots_step_disabled_writes_no_plots(classification_env) -> None:
    storage, job, _df, _output = classification_env
    job = job.model_copy(
        update={"options": EvaluationOptions(plots=False, global_methods=[])}
    )
    ctx = load_context(job, storage, n_jobs=1)

    assert not PlotsStep().enabled(job.options)
    run_steps(ctx, progress=lambda *_: None)

    assert all(artifact["kind"] != "plot" for artifact in ctx.artifacts)


def test_plots_step_skips_roc_for_single_class_test_set(
    classification_env, caplog
) -> None:
    storage, job, df, output = classification_env
    df = df.copy()
    df.loc[output.test_index, "target"] = df.loc[output.test_index[0], "target"]
    storage.put_bytes(
        job.dataset_key, df.to_csv(index=False).encode("utf-8"), "text/csv"
    )
    ctx = load_context(job, storage, n_jobs=1)

    with caplog.at_level(logging.WARNING):
        PlotsStep().run(ctx)

    written_keys = {artifact["key"] for artifact in ctx.artifacts}
    assert "experiments/e/evaluations/v/plots/roc_curve.png" not in written_keys
    assert "experiments/e/evaluations/v/plots/precision_recall_curve.png" not in (
        written_keys
    )
    assert "only one class" in caplog.text
