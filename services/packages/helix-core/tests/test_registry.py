"""The hard-rule test: every registry step fits, converts to ONNX and passes parity."""

from typing import Any

import numpy as np
import pytest
from sklearn.datasets import make_classification, make_regression

from helix_core.messages import PipelineSpec, ProblemType
from helix_core.onnx_export import check_parity, to_onnx
from helix_core.pipelines import REGISTRY, build_pipeline, catalog

N_SAMPLES = 150
N_FEATURES = 8

# Preprocessors and selectors are tested in front of a linear model of the right type.
_DEFAULT_MODEL: dict[ProblemType, str] = {
    "classification": "logistic_regression",
    "regression": "ridge",
}


def _data(problem_type: ProblemType) -> tuple[np.ndarray, np.ndarray]:
    if problem_type == "classification":
        return make_classification(
            n_samples=N_SAMPLES,
            n_features=N_FEATURES,
            n_informative=5,
            random_state=42,
        )
    return make_regression(
        n_samples=N_SAMPLES, n_features=N_FEATURES, noise=0.1, random_state=42
    )


def _spec(step_id: str, problem_type: ProblemType) -> PipelineSpec:
    definition = REGISTRY[step_id]
    steps: list[dict[str, Any]] = []
    model = step_id
    if definition.kind != "model":
        steps = [{"step": step_id, "params": {}}]
        model = _DEFAULT_MODEL[problem_type]
    return PipelineSpec(
        problem_type=problem_type,
        target_column="y",
        feature_columns=[f"f{i}" for i in range(N_FEATURES)],
        steps=steps,
        model={"step": model, "params": {}},
    )


_CASES = [
    (step_id, problem_type)
    for step_id, definition in sorted(REGISTRY.items())
    for problem_type in definition.problem_types
]


@pytest.mark.parametrize(("step_id", "problem_type"), _CASES)
def test_registry_step_fits_converts_and_passes_parity(
    step_id: str, problem_type: ProblemType
) -> None:
    x, y = _data(problem_type)
    pipeline = build_pipeline(_spec(step_id, problem_type), N_FEATURES, n_jobs=1)
    pipeline.fit(x, y)

    onnx_bytes = to_onnx(pipeline, N_FEATURES, problem_type)

    check_parity(pipeline, onnx_bytes, x, problem_type)


@pytest.mark.parametrize("step_id", sorted(REGISTRY))
def test_registry_step_params_all_have_defaults(step_id: str) -> None:
    REGISTRY[step_id].params_model.model_validate({})


@pytest.mark.parametrize("step_id", sorted(REGISTRY))
def test_registry_search_grid_only_for_models_and_values_are_valid(
    step_id: str,
) -> None:
    definition = REGISTRY[step_id]
    if definition.kind != "model":
        assert definition.search_grid is None
        return

    assert definition.search_grid is not None
    for key, values in definition.search_grid.items():
        for value in values:
            definition.params_model.model_validate({key: value})


def test_catalog_sorts_steps_by_kind_then_id() -> None:
    steps = catalog()["steps"]
    kinds = ["preprocessor", "feature_selector", "model"]

    assert [s["id"] for s in steps] == [
        s["id"] for s in sorted(steps, key=lambda s: (kinds.index(s["kind"]), s["id"]))
    ]
    assert {s["id"] for s in steps} == set(REGISTRY)


def test_catalog_entries_have_a_params_schema_and_problem_types() -> None:
    entry = next(s for s in catalog()["steps"] if s["id"] == "random_forest_regressor")

    assert entry["problem_types"] == ["regression"]
    assert entry["params_schema"]["additionalProperties"] is False
    assert entry["params_schema"]["properties"]["n_estimators"]["maximum"] == 1000
    assert entry["search_grid"]["max_depth"] == [None, 10, 20, 30]
