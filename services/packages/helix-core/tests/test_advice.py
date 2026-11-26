import pytest

from helix_core.messages import PipelineSpec
from helix_core.pipelines import redundant_scaler_steps


def _spec(steps: list[str], model: str) -> PipelineSpec:
    problem_type = (
        "regression"
        if model.endswith("_regressor") or model == "ridge"
        else "classification"
    )
    return PipelineSpec.model_validate(
        {
            "problem_type": problem_type,
            "target_column": "y",
            "feature_columns": ["a", "b"],
            "steps": [{"step": step} for step in steps],
            "model": {"step": model},
        }
    )


@pytest.mark.parametrize(
    ("steps", "model", "expected"),
    [
        (["min_max_scaler"], "xgboost_classifier", ["min_max_scaler"]),
        (["standard_scaler", "lasso_selector"], "random_forest_classifier", []),
        (
            ["mean_centering", "variance_threshold"],
            "xgboost_regressor",
            ["mean_centering"],
        ),
        (["standard_scaler", "variance_threshold"], "xgboost_regressor", []),
        (["standard_scaler"], "logistic_regression", []),
        (
            [
                "simple_imputer",
                "robust_scaler",
                "select_k_best",
                "correlation_threshold",
            ],
            "random_forest_regressor",
            ["robust_scaler"],
        ),
        (
            ["standard_scaler", "lasso_selector", "min_max_scaler"],
            "random_forest_classifier",
            ["min_max_scaler"],
        ),
        ([], "xgboost_classifier", []),
    ],
)
def test_redundant_scaler_steps_finds_scalers_with_no_effect_on_a_tree_model(
    steps: list[str], model: str, expected: list[str]
) -> None:
    assert redundant_scaler_steps(_spec(steps, model)) == expected
