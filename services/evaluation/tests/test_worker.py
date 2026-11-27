from helix_core.settings import HelixSettings
from helix_evaluation.worker import EvaluationWorker


def test_handle_returns_test_metrics_and_metrics_artifact(classification_env) -> None:
    storage, job, _df, _output = classification_env
    worker = EvaluationWorker(HelixSettings(), storage=storage)
    events = []

    result = worker.handle(
        job, lambda status, **kwargs: events.append((status, kwargs))
    )

    assert set(result) == {"metrics", "artifacts"}
    assert set(result["metrics"]) == {
        "accuracy",
        "f1_weighted",
        "precision_weighted",
        "recall_weighted",
        "mcc",
        "roc_auc",
    }
    assert result["artifacts"][0] == {
        "key": "experiments/e/evaluations/v/metrics.json",
        "kind": "json",
        "title": "Metrics",
    }
    assert {artifact["kind"] for artifact in result["artifacts"][1:]} == {
        "table",
        "plot",
    }
    written = storage.get_json("experiments/e/evaluations/v/metrics.json")
    assert written["test"] == result["metrics"]
    assert [status for status, _ in events] == ["progress", "progress"]


def test_handle_regression(regression_env) -> None:
    storage, job, _df, _output = regression_env
    worker = EvaluationWorker(HelixSettings(), storage=storage)

    result = worker.handle(job, lambda status, **kwargs: None)

    assert set(result["metrics"]) == {"r2", "mae", "rmse"}


def test_entity_id_returns_evaluation_id(classification_env) -> None:
    storage, job, _df, _output = classification_env
    worker = EvaluationWorker(HelixSettings(), storage=storage)

    assert worker.entity_id(job) == job.evaluation_id
