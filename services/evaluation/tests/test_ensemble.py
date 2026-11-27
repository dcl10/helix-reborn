import math

import pandas as pd
import pytest

from helix_core.errors import InvalidSpecError
from helix_core.messages import EvaluationOptions
from helix_core.storage import Storage
from helix_evaluation.context import load_context
from helix_evaluation.fi.ensemble import (
    EnsembleStep,
    ensemble_majority_vote,
    ensemble_mean,
)
from helix_evaluation.steps import GlobalImportanceStep

PREFIX = "experiments/e/evaluations/v/"

# 3 features x 2 methods. Ranks (dense, descending): Permutation a1 b2 c3,
# SHAP b1 a2 c3. a and b tie on {1, 2}, so both of their values count; c's mode
# is 3 in both. With two methods the majority vote therefore equals the mean.
TWO_METHODS = pd.DataFrame(
    {"Permutation": [1.0, 0.5, 0.0], "SHAP": [0.6, 1.0, 0.0]},
    index=["a", "b", "c"],
)

# 3 features x 3 methods. Ranks: a [1, 1, 2] -> mode 1, keeps M1 and M2;
# b [2, 3, 1] -> three-way tie, only the first two modes (1, 2) are kept, so M1 and
# M3 count; c [3, 2, 3] -> mode 3, keeps M1 and M3.
THREE_METHODS = pd.DataFrame(
    {"M1": [1.0, 0.5, 0.0], "M2": [1.0, 0.0, 0.4], "M3": [0.2, 1.0, 0.0]},
    index=["a", "b", "c"],
)


def test_ensemble_mean_two_methods_golden() -> None:
    mean_df, std_df = ensemble_mean({"model": TWO_METHODS})

    assert list(mean_df.columns) == ["Mean Importance"]
    assert list(mean_df.index) == ["a", "b", "c"]
    assert mean_df["Mean Importance"].tolist() == pytest.approx([0.8, 0.75, 0.0])
    assert list(std_df.index) == ["a", "b", "c"]
    assert std_df["Mean Importance"].isna().all()


def test_ensemble_majority_vote_two_methods_golden() -> None:
    vote_df, _ = ensemble_majority_vote({"model": TWO_METHODS})

    assert list(vote_df.columns) == ["Majority Vote Importance"]
    assert list(vote_df.index) == ["a", "b", "c"]
    assert vote_df["Majority Vote Importance"].tolist() == pytest.approx(
        [0.8, 0.75, 0.0]
    )


def test_ensemble_mean_three_methods_golden() -> None:
    mean_df, _ = ensemble_mean({"model": THREE_METHODS})

    assert mean_df["Mean Importance"].to_dict() == pytest.approx(
        {"a": 2.2 / 3, "b": 0.5, "c": 0.4 / 3}
    )


def test_ensemble_majority_vote_three_methods_keeps_two_tied_ranks() -> None:
    vote_df, _ = ensemble_majority_vote({"model": THREE_METHODS})

    # a: (1.0 + 1.0 + 0) / 3; b: (0.5 + 0 + 1.0) / 3; c: (0 + 0 + 0) / 3
    assert list(vote_df.index) == ["a", "b", "c"]
    assert vote_df["Majority Vote Importance"].tolist() == pytest.approx(
        [2 / 3, 0.5, 0.0]
    )


def test_ensemble_mean_empty_returns_empty_frame() -> None:
    mean_df, std_df = ensemble_mean({"model": pd.DataFrame()})

    assert mean_df.empty
    assert list(mean_df.columns) == ["Mean Importance"]
    assert std_df.empty


def test_ensemble_majority_vote_empty_returns_empty_frame() -> None:
    vote_df, std_df = ensemble_majority_vote({"model": pd.DataFrame()})

    assert vote_df.empty
    assert list(vote_df.columns) == ["Majority Vote Importance"]
    assert std_df.empty


def test_run_without_global_importance_raises(
    s3_storage: Storage, fi_regression_env
) -> None:
    options = EvaluationOptions(global_methods=[], ensemble_methods=["mean"])
    job, _ = fi_regression_env(options)
    ctx = load_context(job, s3_storage, n_jobs=1)

    with pytest.raises(InvalidSpecError, match="at least one global method"):
        EnsembleStep().run(ctx)


def test_run_writes_tables_plots_and_results(
    s3_storage: Storage, fi_regression_env
) -> None:
    options = EvaluationOptions(
        global_methods=["permutation", "shap"],
        permutation_repeats=2,
        ensemble_methods=["mean", "majority_vote"],
    )
    job, _ = fi_regression_env(options)
    ctx = load_context(job, s3_storage, n_jobs=1)
    GlobalImportanceStep().run(ctx)
    ctx.artifacts.clear()

    EnsembleStep().run(ctx)

    assert {a["key"]: a["kind"] for a in ctx.artifacts} == {
        f"{PREFIX}tables/ensemble_mean.csv": "table",
        f"{PREFIX}plots/ensemble_mean.png": "plot",
        f"{PREFIX}tables/ensemble_majority_vote.csv": "table",
        f"{PREFIX}plots/ensemble_majority_vote.png": "plot",
    }
    results = ctx.results["ensemble_importance"]
    assert set(results) == {"Mean", "Majority Vote"}
    assert set(results["Mean"].head(2).index) == {"x0", "x1"}
    assert math.isclose(results["Mean"]["Mean Importance"].max(), 1.0)


def test_run_only_runs_selected_methods(s3_storage: Storage, fi_regression_env) -> None:
    options = EvaluationOptions(global_methods=["shap"], ensemble_methods=["mean"])
    job, _ = fi_regression_env(options)
    ctx = load_context(job, s3_storage, n_jobs=1)
    GlobalImportanceStep().run(ctx)

    EnsembleStep().run(ctx)

    assert set(ctx.results["ensemble_importance"]) == {"Mean"}
    assert not any("majority" in a["key"] for a in ctx.artifacts)


def test_ensemble_step_enabled_requires_ensemble_methods() -> None:
    step = EnsembleStep()

    assert step.enabled(EvaluationOptions()) is False
    assert step.enabled(EvaluationOptions(ensemble_methods=["mean"])) is True
