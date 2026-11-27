from helix_core.messages import EvaluationOptions
from helix_core.storage import Storage
from helix_evaluation.context import load_context
from helix_evaluation.fi.local_methods import lime_values, local_shap_values
from helix_evaluation.steps import LocalImportanceStep

FEATURES = [f"x{i}" for i in range(6)]
PREFIX = "experiments/e/evaluations/v/"


def _top_two(df) -> set[str]:
    return set(df.abs().mean().sort_values(ascending=False).head(2).index)


def test_lime_values_rank_true_features_top(
    s3_storage: Storage, fi_regression_env
) -> None:
    job, _ = fi_regression_env(EvaluationOptions(local_methods=["lime"]))
    ctx = load_context(job, s3_storage, n_jobs=1)
    x_explain = ctx.x_test.head(8)

    lime_df = lime_values(ctx.model, ctx.x_train, x_explain, "regression", 0)

    assert _top_two(lime_df) == {"x0", "x1"}


def test_local_shap_values_rank_true_features_top(
    s3_storage: Storage, fi_regression_env
) -> None:
    job, _ = fi_regression_env(EvaluationOptions(local_methods=["shap"]))
    ctx = load_context(job, s3_storage, n_jobs=1)
    x_explain = ctx.x_test.head(8)

    shap_df, explanation = local_shap_values(ctx.model, ctx.x_train.head(50), x_explain)

    assert _top_two(shap_df) == {"x0", "x1"}
    assert explanation.values.shape == (8, len(FEATURES))


def test_lime_values_binary_classification_uses_predict_proba(
    s3_storage: Storage, fi_classification_env
) -> None:
    job, _ = fi_classification_env(EvaluationOptions(local_methods=["lime"]))
    ctx = load_context(job, s3_storage, n_jobs=1)
    x_explain = ctx.x_test.head(6)

    lime_df = lime_values(ctx.model, ctx.x_train, x_explain, "classification", 0)

    assert list(lime_df.columns) == FEATURES
    assert list(lime_df.index) == list(x_explain.index)
    assert _top_two(lime_df) == {"x0", "x1"}


def test_lime_values_multiclass_explains_top_label(
    s3_storage: Storage, fi_classification_env
) -> None:
    job, _ = fi_classification_env(
        EvaluationOptions(local_methods=["lime"]), n_classes=3
    )
    ctx = load_context(job, s3_storage, n_jobs=1)

    lime_df = lime_values(
        ctx.model, ctx.x_train, ctx.x_test.head(4), "classification", 0
    )

    assert lime_df.shape == (4, len(FEATURES))
    assert lime_df.notna().all().all()


def test_run_writes_tables_plots_and_results(
    s3_storage: Storage, fi_regression_env
) -> None:
    options = EvaluationOptions(local_methods=["lime", "shap"], local_max_samples=5)
    job, _ = fi_regression_env(options)
    ctx = load_context(job, s3_storage, n_jobs=1)

    LocalImportanceStep().run(ctx)

    kinds = {artifact["key"]: artifact["kind"] for artifact in ctx.artifacts}
    assert kinds == {
        f"{PREFIX}tables/local_lime.csv": "table",
        f"{PREFIX}plots/local_lime_violin.png": "plot",
        f"{PREFIX}tables/local_shap.csv": "table",
        f"{PREFIX}plots/local_shap_violin.png": "plot",
        f"{PREFIX}plots/local_shap_waterfall_0.png": "plot",
    }
    results = ctx.results["local_importance"]
    assert set(results) == {"LIME", "SHAP"}
    for df in results.values():
        assert df.shape == (5, len(FEATURES))
        assert list(df.columns) == FEATURES
    assert list(results["LIME"].index) == list(results["SHAP"].index)


def test_run_only_runs_selected_methods(s3_storage: Storage, fi_regression_env) -> None:
    options = EvaluationOptions(local_methods=["shap"], local_max_samples=5)
    job, _ = fi_regression_env(options)
    ctx = load_context(job, s3_storage, n_jobs=1)

    LocalImportanceStep().run(ctx)

    assert set(ctx.results["local_importance"]) == {"SHAP"}
    assert not any("lime" in artifact["key"] for artifact in ctx.artifacts)


def test_run_caps_samples_at_test_set_size(
    s3_storage: Storage, fi_regression_env
) -> None:
    options = EvaluationOptions(local_methods=["shap"], local_max_samples=1000)
    job, _ = fi_regression_env(options, n_rows=40)
    ctx = load_context(job, s3_storage, n_jobs=1)

    LocalImportanceStep().run(ctx)

    assert len(ctx.results["local_importance"]["SHAP"]) == len(ctx.x_test)


def test_local_importance_step_enabled_requires_local_methods() -> None:
    step = LocalImportanceStep()

    assert step.enabled(EvaluationOptions()) is False
    assert step.enabled(EvaluationOptions(local_methods=["lime"])) is True
