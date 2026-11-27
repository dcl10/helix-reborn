from io import BytesIO

import pandas as pd
import pytest

from helix_core.messages import EvaluationOptions
from helix_core.storage import Storage
from helix_evaluation.context import load_context
from helix_evaluation.fi.global_methods import normalise
from helix_evaluation.steps import GlobalImportanceStep

FEATURES = [f"x{i}" for i in range(6)]


def test_normalise_scales_to_unit_range() -> None:
    series = pd.Series([1.0, 2.0, 4.0], index=["a", "b", "c"])

    normalised = normalise(series)

    assert normalised["a"] == 0.0
    assert normalised["c"] == 1.0
    assert normalised["b"] == pytest.approx(1 / 3)


def test_normalise_constant_series_is_all_zero() -> None:
    series = pd.Series([5.0, 5.0, 5.0], index=["a", "b", "c"])

    normalised = normalise(series)

    assert (normalised == 0.0).all()


def test_permutation_and_shap_rank_true_features_top(
    s3_storage: Storage, fi_regression_env
) -> None:
    options = EvaluationOptions(
        global_methods=["permutation", "shap"],
        permutation_repeats=5,
        num_features_to_plot=6,
    )
    job, _df = fi_regression_env(options)
    ctx = load_context(job, s3_storage, n_jobs=1)

    GlobalImportanceStep().run(ctx)

    perm_df = pd.read_csv(
        BytesIO(
            s3_storage.get_bytes(
                "experiments/e/evaluations/v/tables/global_permutation.csv"
            )
        ),
        index_col=0,
    )
    shap_df = pd.read_csv(
        BytesIO(
            s3_storage.get_bytes("experiments/e/evaluations/v/tables/global_shap.csv")
        ),
        index_col=0,
    )

    perm_top_two = set(
        perm_df.sort_values("importance_mean", ascending=False).head(2).index
    )
    shap_top_two = set(
        shap_df.sort_values("mean_abs_shap", ascending=False).head(2).index
    )
    assert perm_top_two == {"x0", "x1"}
    assert shap_top_two == {"x0", "x1"}

    importance = ctx.results["global_importance"]
    assert set(importance.columns) == {"Permutation", "SHAP"}
    assert set(importance.index) == set(FEATURES)


def test_classification_run_writes_tables_and_plots(
    s3_storage: Storage, fi_classification_env
) -> None:
    options = EvaluationOptions(
        global_methods=["permutation", "shap"],
        permutation_repeats=5,
        num_features_to_plot=6,
    )
    job, _df = fi_classification_env(options)
    ctx = load_context(job, s3_storage, n_jobs=1)

    GlobalImportanceStep().run(ctx)

    kinds = {artifact["key"]: artifact["kind"] for artifact in ctx.artifacts}
    assert kinds["experiments/e/evaluations/v/tables/global_permutation.csv"] == "table"
    assert kinds["experiments/e/evaluations/v/tables/global_shap.csv"] == "table"
    assert kinds["experiments/e/evaluations/v/plots/global_permutation.png"] == "plot"
    assert kinds["experiments/e/evaluations/v/plots/global_shap_bar.png"] == "plot"
    assert kinds["experiments/e/evaluations/v/plots/global_shap_beeswarm.png"] == "plot"


def test_shap_only_writes_only_shap_outputs(
    s3_storage: Storage, fi_regression_env
) -> None:
    options = EvaluationOptions(
        global_methods=["shap"], permutation_repeats=5, num_features_to_plot=6
    )
    job, _df = fi_regression_env(options)
    ctx = load_context(job, s3_storage, n_jobs=1)

    GlobalImportanceStep().run(ctx)

    keys = [artifact["key"] for artifact in ctx.artifacts]
    assert not any("permutation" in key for key in keys)
    assert any("global_shap" in key for key in keys)
    assert set(ctx.results["global_importance"].columns) == {"SHAP"}


def test_global_importance_step_enabled_requires_global_methods() -> None:
    step = GlobalImportanceStep()

    assert step.enabled(EvaluationOptions(global_methods=[])) is False
    assert step.enabled(EvaluationOptions(global_methods=["shap"])) is True
