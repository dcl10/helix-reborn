import logging
from io import BytesIO

import pandas as pd
import pytest

from helix_core.messages import EvaluationOptions, FuzzyOptions, PlotOptions
from helix_core.storage import Storage
from helix_evaluation.context import EvaluationContext, load_context
from helix_evaluation.fi.fuzzy import FuzzyInterpreter, FuzzyStep, sanitise_name
from helix_evaluation.steps import run_steps

PREFIX = "experiments/e/evaluations/v/"


def _options(**fuzzy: object) -> EvaluationOptions:
    return EvaluationOptions(
        plots=False,
        global_methods=["permutation", "shap"],
        permutation_repeats=2,
        local_methods=["lime", "shap"],
        local_max_samples=12,
        ensemble_methods=["mean", "majority_vote"],
        fuzzy=FuzzyOptions(**fuzzy),
    )


def _run(storage: Storage, make_env, options: EvaluationOptions, **env_kwargs):
    job, _ = make_env(options, **env_kwargs)
    ctx = load_context(job, storage, n_jobs=1)
    run_steps(ctx, progress=lambda *_: None)
    return ctx


def _table(storage: Storage, name: str) -> pd.DataFrame:
    body = storage.get_bytes(f"{PREFIX}tables/{name}.csv")
    return pd.read_csv(BytesIO(body), index_col=0)


def _plot_names(ctx: EvaluationContext) -> set[str]:
    return {
        a["key"].removeprefix(f"{PREFIX}plots/").removesuffix(".png")
        for a in ctx.artifacts
        if a["kind"] == "plot"
    }


def test_run_regression_writes_rules_and_plots(
    s3_storage: Storage, fi_regression_env
) -> None:
    options = _options(
        number_fuzzy_features=3,
        number_clusters=3,
        cluster_names=["low", "medium", "high"],
        number_rules=2,
    )
    ctx = _run(s3_storage, fi_regression_env, options, n_rows=150)

    rules = _table(s3_storage, "fuzzy_rules")
    contextual = _table(s3_storage, "fuzzy_contextual_rules")
    assert not rules.empty
    assert not contextual.empty
    assert rules.groupby("target").size().max() <= 2
    assert set(rules["target"]) <= {"low", "medium", "high"}
    assert set(contextual.columns) <= {"low", "medium", "high"}

    selected = list(ctx.results["ensemble_importance"]["Majority Vote"].index[:3])
    plots = _plot_names(ctx)
    assert {f"fuzzy_sets_{feature}" for feature in selected} <= plots
    assert len([p for p in plots if p.startswith("fuzzy_sets_")]) == 3
    assert "fuzzy_target_clusters" in plots


def test_run_number_fuzzy_features_limits_rule_features(
    s3_storage: Storage, fi_regression_env
) -> None:
    options = _options(number_fuzzy_features=2)
    _run(s3_storage, fi_regression_env, options, n_rows=150)

    rules = _table(s3_storage, "fuzzy_rules")
    contextual = _table(s3_storage, "fuzzy_contextual_rules")
    assert set(rules.columns) == {"x0", "x1", "target", "records"}
    assert set(contextual.index) == {"x0", "x1"}


def test_run_without_granular_features_skips_membership_plots(
    s3_storage: Storage, fi_regression_env
) -> None:
    options = _options(number_fuzzy_features=3, granular_features=False)
    ctx = _run(s3_storage, fi_regression_env, options, n_rows=150)

    assert not any(name.startswith("fuzzy_sets_") for name in _plot_names(ctx))
    assert not _table(s3_storage, "fuzzy_contextual_rules").empty


def test_run_classification_uses_classes_as_contexts(
    s3_storage: Storage, fi_classification_env
) -> None:
    options = _options(number_fuzzy_features=3)
    ctx = _run(s3_storage, fi_classification_env, options, n_rows=150, n_classes=3)

    rules = _table(s3_storage, "fuzzy_rules")
    assert set(rules["target"]) <= {"low", "mid", "high"}
    assert rules.groupby("target").size().max() <= options.fuzzy.number_rules
    assert "fuzzy_target_clusters" not in _plot_names(ctx)


def test_select_features_falls_back_to_mean_with_warning(caplog) -> None:
    interpreter = FuzzyInterpreter(
        FuzzyOptions(number_fuzzy_features=1), "regression", PlotOptions()
    )
    mean = pd.DataFrame({"Mean Importance": [0.2, 0.9]}, index=["a", "b"])

    with caplog.at_level(logging.WARNING):
        selected = interpreter._select_features({"Mean": mean})

    assert selected == ["b"]
    assert "Majority Vote" in caplog.text


def test_select_features_majority_vote_first() -> None:
    interpreter = FuzzyInterpreter(
        FuzzyOptions(number_fuzzy_features=1), "regression", PlotOptions()
    )
    mean = pd.DataFrame({"Mean Importance": [0.2, 0.9]}, index=["a", "b"])
    vote = pd.DataFrame({"Majority Vote Importance": [0.9, 0.2]}, index=["a", "b"])

    assert interpreter._select_features({"Mean": mean, "Majority Vote": vote}) == ["a"]


@pytest.mark.parametrize(
    ("name", "expected"),
    [("x0", "x0"), ("weight (kg)", "weight__kg_"), ("a/b.c", "a_b_c")],
)
def test_sanitise_name_replaces_unsafe_characters(name: str, expected: str) -> None:
    assert sanitise_name(name) == expected


def test_fuzzy_step_enabled_requires_fuzzy_options() -> None:
    assert FuzzyStep().enabled(EvaluationOptions()) is False
    assert FuzzyStep().enabled(_options()) is True
