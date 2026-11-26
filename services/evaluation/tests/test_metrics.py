import numpy as np

from helix_evaluation.context import load_context
from helix_evaluation.metrics import (
    MetricsStep,
    _classification_metrics,
    _regression_metrics,
)


def test_classification_metrics_known_values() -> None:
    y_true = np.array([0, 0, 1, 1])
    y_pred = np.array([0, 1, 1, 1])
    proba = np.array([[0.9, 0.1], [0.4, 0.6], [0.2, 0.8], [0.1, 0.9]])

    metrics = _classification_metrics(y_true, y_pred, proba)

    assert metrics["accuracy"] == 0.75
    assert metrics["precision_weighted"] is not None
    assert metrics["recall_weighted"] is not None
    assert metrics["f1_weighted"] is not None
    assert metrics["mcc"] is not None
    assert metrics["roc_auc"] == 1.0


def test_classification_metrics_multiclass_roc_auc() -> None:
    y_true = np.array([0, 1, 2, 1, 0, 2])
    y_pred = np.array([0, 1, 2, 0, 0, 2])
    proba = np.array(
        [
            [0.8, 0.1, 0.1],
            [0.1, 0.8, 0.1],
            [0.1, 0.1, 0.8],
            [0.5, 0.3, 0.2],
            [0.7, 0.2, 0.1],
            [0.1, 0.2, 0.7],
        ]
    )

    metrics = _classification_metrics(y_true, y_pred, proba)

    assert metrics["roc_auc"] is not None


def test_classification_metrics_single_class_roc_auc_is_none() -> None:
    y_true = np.array([0, 0, 0, 0])
    y_pred = np.array([0, 0, 0, 1])
    proba = np.array([[0.9, 0.1], [0.8, 0.2], [0.7, 0.3], [0.4, 0.6]])

    metrics = _classification_metrics(y_true, y_pred, proba)

    assert metrics["roc_auc"] is None


def test_regression_metrics_known_values() -> None:
    y_true = np.array([1.0, 2.0, 3.0, 4.0])
    y_pred = np.array([1.0, 2.0, 3.0, 4.0])

    metrics = _regression_metrics(y_true, y_pred)

    assert metrics["r2"] == 1.0
    assert metrics["mae"] == 0.0
    assert metrics["rmse"] == 0.0


def test_metrics_step_writes_metrics_json(classification_env) -> None:
    storage, job, _df, _output = classification_env
    ctx = load_context(job, storage, n_jobs=1)

    MetricsStep().run(ctx)

    written = storage.get_json("experiments/e/evaluations/v/metrics.json")
    assert set(written) == {"train", "test"}
    assert ctx.results["metrics"] == written
    assert ctx.artifacts == [
        {
            "key": "experiments/e/evaluations/v/metrics.json",
            "kind": "json",
            "title": "Metrics",
        }
    ]


def test_metrics_step_regression(regression_env) -> None:
    storage, job, _df, _output = regression_env
    ctx = load_context(job, storage, n_jobs=1)

    MetricsStep().run(ctx)

    metrics = ctx.results["metrics"]
    assert set(metrics["test"]) == {"r2", "mae", "rmse"}
    assert set(metrics["train"]) == {"r2", "mae", "rmse"}
