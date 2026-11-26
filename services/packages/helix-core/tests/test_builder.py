from typing import Any

import pytest
from sklearn.feature_selection import SelectKBest

from helix_core.errors import InvalidSpecError
from helix_core.messages import PipelineSpec
from helix_core.pipelines import build_pipeline, search_space, validate_spec


def _spec(
    model: str = "random_forest_regressor",
    model_params: dict[str, Any] | None = None,
    steps: list[dict[str, Any]] | None = None,
    problem_type: str = "regression",
    search: dict[str, Any] | None = None,
) -> PipelineSpec:
    return PipelineSpec.model_validate(
        {
            "problem_type": problem_type,
            "target_column": "y",
            "feature_columns": ["a", "b", "c"],
            "steps": steps or [],
            "model": {"step": model, "params": model_params or {}},
            "search": search,
        }
    )


def test_validate_spec_accepts_a_valid_spec() -> None:
    validate_spec(
        _spec(
            steps=[{"step": "simple_imputer", "params": {"strategy": "mean"}}],
            model_params={"n_estimators": 200},
            search={"method": "grid"},
        )
    )


def test_validate_spec_rejects_an_unknown_step() -> None:
    with pytest.raises(InvalidSpecError, match="Unknown step 'magic'"):
        validate_spec(_spec(steps=[{"step": "magic", "params": {}}]))


def test_validate_spec_rejects_a_model_in_steps() -> None:
    with pytest.raises(InvalidSpecError, match="is a model"):
        validate_spec(_spec(steps=[{"step": "ridge", "params": {}}]))


def test_validate_spec_rejects_a_non_model_as_the_model() -> None:
    with pytest.raises(InvalidSpecError, match="isn't a model"):
        validate_spec(_spec(model="standard_scaler"))


def test_validate_spec_rejects_a_model_for_the_wrong_problem_type() -> None:
    with pytest.raises(InvalidSpecError, match="doesn't support regression"):
        validate_spec(_spec(model="logistic_regression"))


@pytest.mark.parametrize(
    "params", [{"n_estimators": 0}, {"n_estimators": "many"}, {"bogus": 1}]
)
def test_validate_spec_rejects_bad_params(params: dict[str, Any]) -> None:
    with pytest.raises(InvalidSpecError, match="Invalid parameters"):
        validate_spec(_spec(model_params=params))


def test_validate_spec_rejects_an_unknown_param_grid_key() -> None:
    with pytest.raises(InvalidSpecError, match="Unknown parameter 'bogus'"):
        validate_spec(_spec(search={"method": "grid", "param_grid": {"bogus": [1, 2]}}))


def test_validate_spec_rejects_an_invalid_param_grid_value() -> None:
    with pytest.raises(InvalidSpecError, match="Invalid value 0"):
        validate_spec(
            _spec(search={"method": "grid", "param_grid": {"n_estimators": [10, 0]}})
        )


def test_validate_spec_rejects_an_empty_param_grid_list() -> None:
    with pytest.raises(InvalidSpecError, match="at least one value"):
        validate_spec(_spec(search={"method": "grid", "param_grid": {"max_depth": []}}))


def test_build_pipeline_names_steps_in_order() -> None:
    pipeline = build_pipeline(
        _spec(
            steps=[
                {"step": "simple_imputer", "params": {}},
                {"step": "standard_scaler", "params": {}},
            ]
        ),
        n_features=3,
        n_jobs=4,
    )

    assert [name for name, _ in pipeline.steps] == [
        "s0_simple_imputer",
        "s1_standard_scaler",
        "model",
    ]


def test_build_pipeline_passes_params_and_the_random_seed() -> None:
    spec = _spec(model_params={"n_estimators": 42, "max_depth": 7})

    model = build_pipeline(spec, n_features=3, n_jobs=1).named_steps["model"]

    assert model.n_estimators == 42
    assert model.max_depth == 7
    assert model.random_state == spec.split.random_seed


def test_build_pipeline_gives_the_model_n_jobs_without_a_search() -> None:
    model = build_pipeline(_spec(), n_features=3, n_jobs=4).named_steps["model"]

    assert model.n_jobs == 4


def test_build_pipeline_gives_the_model_one_job_with_a_search() -> None:
    model = build_pipeline(
        _spec(search={"method": "grid"}), n_features=3, n_jobs=4
    ).named_steps["model"]

    assert model.n_jobs == 1


def test_build_pipeline_clamps_select_k_best_to_n_features() -> None:
    pipeline = build_pipeline(
        _spec(steps=[{"step": "select_k_best", "params": {"k": 50}}]),
        n_features=3,
        n_jobs=1,
    )

    selector = pipeline.named_steps["s0_select_k_best"]
    assert isinstance(selector, SelectKBest)
    assert selector.k == 3


def test_build_pipeline_turns_mlp_layer_sizes_into_a_tuple() -> None:
    model = build_pipeline(
        _spec(model="mlp_regressor", model_params={"hidden_layer_sizes": [20, 10]}),
        n_features=3,
        n_jobs=1,
    ).named_steps["model"]

    assert model.hidden_layer_sizes == (20, 10)


def test_search_space_prefixes_the_default_grid() -> None:
    space = search_space(_spec(search={"method": "grid"}))

    assert space["model__n_estimators"] == [10, 50, 100, 200]
    assert space["model__max_depth"] == [None, 10, 20, 30]
    assert all(key.startswith("model__") for key in space)


def test_search_space_uses_the_param_grid_and_converts_mlp_sizes() -> None:
    space = search_space(
        _spec(
            model="mlp_regressor",
            search={
                "method": "random",
                "n_iter": 5,
                "param_grid": {"hidden_layer_sizes": [[50], [100, 50]]},
            },
        )
    )

    assert space == {"model__hidden_layer_sizes": [(50,), (100, 50)]}


def test_search_space_converts_the_default_mlp_grid() -> None:
    space = search_space(_spec(model="mlp_regressor", search={"method": "grid"}))

    assert space["model__hidden_layer_sizes"] == [(50,), (100,), (100, 50)]


def test_search_space_requires_a_search() -> None:
    with pytest.raises(InvalidSpecError, match="no hyperparameter search"):
        search_space(_spec())
