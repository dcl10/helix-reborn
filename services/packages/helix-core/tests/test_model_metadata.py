from datetime import UTC, datetime
from uuid import uuid4

import pytest
from pydantic import ValidationError

from helix_core.messages import PipelineSpec, StepSpec
from helix_core.model_metadata import ModelMetadata, library_versions

PIPELINE = PipelineSpec(
    problem_type="classification",
    target_column="label",
    feature_columns=["a", "b"],
    model=StepSpec(step="logistic_regression"),
)


def _metadata(**overrides: object) -> ModelMetadata:
    defaults: dict[str, object] = {
        "run_id": uuid4(),
        "problem_type": "classification",
        "target_column": "label",
        "feature_columns": ["a", "b"],
        "id_column": None,
        "classes": ["no", "yes"],
        "onnx_outputs": {"label": "label", "probabilities": "probabilities"},
        "pipeline": PIPELINE,
        "library_versions": library_versions(),
        "helix_version": "dev",
        "created_at": datetime.now(UTC),
    }
    defaults.update(overrides)
    return ModelMetadata(**defaults)


def test_model_metadata_round_trips_json() -> None:
    metadata = _metadata()

    restored = ModelMetadata.model_validate_json(metadata.model_dump_json())

    assert restored == metadata


def test_model_metadata_rejects_extra_field() -> None:
    with pytest.raises(ValidationError):
        ModelMetadata.model_validate(
            _metadata().model_dump() | {"unexpected_field": "oops"}
        )


def test_model_metadata_classes_is_none_for_regression() -> None:
    metadata = _metadata(
        problem_type="regression", classes=None, onnx_outputs={"value": "variable"}
    )

    assert metadata.classes is None


def test_library_versions_has_the_expected_keys() -> None:
    versions = library_versions()

    assert set(versions) == {
        "scikit-learn",
        "skl2onnx",
        "onnx",
        "onnxruntime",
        "xgboost",
        "numpy",
    }
    assert all(isinstance(v, str) and v for v in versions.values())
