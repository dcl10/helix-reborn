from uuid import uuid4

import pytest
from pydantic import ValidationError

from helix_core.messages import (
    EvaluationJob,
    EvaluationOptions,
    FuzzyOptions,
    JobEvent,
    PipelineSpec,
    SearchSpec,
    TrainingJob,
)


def _pipeline_spec(**overrides: object) -> dict:
    spec = {
        "problem_type": "classification",
        "target_column": "label",
        "feature_columns": ["a", "b", "c"],
        "id_column": None,
        "split": {"test_size": 0.2, "random_seed": 42, "cv_folds": 5},
        "steps": [
            {"step": "simple_imputer", "params": {"strategy": "median"}},
            {"step": "standard_scaler", "params": {}},
        ],
        "model": {"step": "random_forest_classifier", "params": {"n_estimators": 200}},
        "search": {"method": "grid", "cv_folds": 5, "n_iter": None, "param_grid": None},
    }
    spec.update(overrides)
    return spec


def _training_job(**overrides: object) -> dict:
    job = {
        "schema_version": 1,
        "job_id": str(uuid4()),
        "run_id": str(uuid4()),
        "experiment_id": str(uuid4()),
        "dataset_key": "experiments/e/datasets/d/data.csv",
        "output_prefix": "experiments/e/runs/r/",
        "pipeline": _pipeline_spec(),
    }
    job.update(overrides)
    return job


def _evaluation_options(**overrides: object) -> dict:
    options = {
        "plots": True,
        "global_methods": ["permutation", "shap"],
        "permutation_repeats": 10,
        "local_methods": ["lime", "shap"],
        "local_max_samples": 50,
        "ensemble_methods": ["mean", "majority_vote"],
        "num_features_to_plot": 10,
        "fuzzy": None,
        "plot_options": None,
    }
    options.update(overrides)
    return options


def _evaluation_job(**overrides: object) -> dict:
    job = {
        "schema_version": 1,
        "job_id": str(uuid4()),
        "evaluation_id": str(uuid4()),
        "run_id": str(uuid4()),
        "experiment_id": str(uuid4()),
        "dataset_key": "experiments/e/datasets/d/data.csv",
        "model_prefix": "experiments/e/runs/r/",
        "output_prefix": "experiments/e/evaluations/v/",
        "options": _evaluation_options(),
    }
    job.update(overrides)
    return job


def test_training_job_parses_the_architecture_doc_example() -> None:
    job = TrainingJob.model_validate(_training_job())

    assert job.schema_version == 1
    assert job.pipeline.problem_type == "classification"
    assert job.pipeline.model.step == "random_forest_classifier"


def test_evaluation_job_parses_the_architecture_doc_example() -> None:
    job = EvaluationJob.model_validate(_evaluation_job())

    assert job.schema_version == 1
    assert job.options.global_methods == ["permutation", "shap"]


def test_job_event_parses_the_architecture_doc_example() -> None:
    event = JobEvent.model_validate(
        {
            "schema_version": 1,
            "job_id": str(uuid4()),
            "job_type": "training",
            "entity_id": str(uuid4()),
            "status": "progress",
            "progress": 0.4,
            "message": "Cross-validating (fold 2 of 5)",
            "result": None,
            "error": None,
            "timestamp": "2026-11-18T12:00:00Z",
        }
    )

    assert event.status == "progress"
    assert event.progress == 0.4


def test_training_job_rejects_an_unknown_field() -> None:
    with pytest.raises(ValidationError):
        TrainingJob.model_validate(_training_job(unexpected_field="nope"))


def test_pipeline_spec_rejects_an_unknown_field() -> None:
    with pytest.raises(ValidationError):
        PipelineSpec.model_validate(_pipeline_spec(unexpected_field="nope"))


def test_search_spec_rejects_random_method_without_n_iter() -> None:
    with pytest.raises(ValidationError):
        SearchSpec.model_validate({"method": "random", "n_iter": None})


def test_search_spec_accepts_random_method_with_n_iter() -> None:
    search = SearchSpec.model_validate({"method": "random", "n_iter": 20})

    assert search.n_iter == 20


def test_pipeline_spec_rejects_target_column_in_feature_columns() -> None:
    with pytest.raises(ValidationError):
        PipelineSpec.model_validate(
            _pipeline_spec(target_column="a", feature_columns=["a", "b", "c"])
        )


def test_pipeline_spec_rejects_id_column_in_feature_columns() -> None:
    with pytest.raises(ValidationError):
        PipelineSpec.model_validate(
            _pipeline_spec(id_column="a", feature_columns=["a", "b", "c"])
        )


def test_pipeline_spec_rejects_duplicate_feature_columns() -> None:
    with pytest.raises(ValidationError):
        PipelineSpec.model_validate(_pipeline_spec(feature_columns=["a", "a", "b"]))


def test_evaluation_options_rejects_fuzzy_without_local_or_ensemble_methods() -> None:
    with pytest.raises(ValidationError):
        EvaluationOptions.model_validate(
            _evaluation_options(
                local_methods=[],
                ensemble_methods=[],
                fuzzy={
                    "number_fuzzy_features": 10,
                    "granular_features": True,
                    "number_clusters": 5,
                    "cluster_names": ["very low", "low", "medium", "high", "very high"],
                    "number_rules": 5,
                },
            )
        )


def test_evaluation_options_accepts_fuzzy_with_local_and_ensemble_methods() -> None:
    options = EvaluationOptions.model_validate(
        _evaluation_options(
            local_methods=["lime"],
            ensemble_methods=["mean"],
            fuzzy={
                "number_fuzzy_features": 10,
                "granular_features": True,
                "number_clusters": 5,
                "cluster_names": ["very low", "low", "medium", "high", "very high"],
                "number_rules": 5,
            },
        )
    )

    assert options.fuzzy is not None


def test_fuzzy_options_rejects_cluster_names_length_mismatch() -> None:
    with pytest.raises(ValidationError):
        FuzzyOptions.model_validate(
            {
                "number_clusters": 5,
                "cluster_names": ["very low", "low"],
            }
        )


def test_job_event_serialises_timestamp_as_iso_8601_with_timezone() -> None:
    event = JobEvent.model_validate(
        {
            "job_id": str(uuid4()),
            "job_type": "training",
            "entity_id": str(uuid4()),
            "status": "started",
        }
    )

    payload = event.model_dump(mode="json")

    assert event.timestamp.tzinfo is not None
    assert event.timestamp.utcoffset().total_seconds() == 0
    timestamp_str = payload["timestamp"]
    assert "+00:00" in timestamp_str or timestamp_str.endswith("Z")
