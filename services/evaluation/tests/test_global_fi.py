from io import BytesIO
from uuid import uuid4

import numpy as np
import pandas as pd
import pytest

from helix_core.messages import (
    EvaluationJob,
    EvaluationOptions,
    PipelineSpec,
    TrainingJob,
)
from helix_core.storage import Storage, join_key
from helix_evaluation.context import load_context
from helix_evaluation.fi.global_methods import normalise
from helix_evaluation.steps import GlobalImportanceStep
from helix_training.train import train

FEATURES = [f"x{i}" for i in range(6)]
N_ROWS = 200


def _regression_env(
    storage: Storage, options: EvaluationOptions
) -> tuple[EvaluationJob, pd.DataFrame]:
    rng = np.random.default_rng(42)
    x = rng.standard_normal((N_ROWS, len(FEATURES)))
    y = 3 * x[:, 0] + 2 * x[:, 1] + 0.1 * rng.standard_normal(N_ROWS)
    df = pd.DataFrame(x, columns=FEATURES)
    df["target"] = y
    return _train_and_upload(storage, "regression", df, "linear_regression", options)


def _classification_env(
    storage: Storage, options: EvaluationOptions
) -> tuple[EvaluationJob, pd.DataFrame]:
    rng = np.random.default_rng(42)
    x = rng.standard_normal((N_ROWS, len(FEATURES)))
    logit = 3 * x[:, 0] + 2 * x[:, 1]
    y = (logit + rng.standard_normal(N_ROWS) > 0).astype(int)
    df = pd.DataFrame(x, columns=FEATURES)
    df["target"] = [("no", "yes")[label] for label in y]
    return _train_and_upload(
        storage, "classification", df, "logistic_regression", options
    )


def _train_and_upload(
    storage: Storage,
    problem_type: str,
    df: pd.DataFrame,
    model_step: str,
    options: EvaluationOptions,
) -> tuple[EvaluationJob, pd.DataFrame]:
    dataset_key = "experiments/e/datasets/d/data.csv"
    model_prefix = "experiments/e/runs/r/"
    training_job = TrainingJob(
        job_id=uuid4(),
        run_id=uuid4(),
        experiment_id=uuid4(),
        dataset_key=dataset_key,
        output_prefix=model_prefix,
        pipeline=PipelineSpec.model_validate(
            {
                "problem_type": problem_type,
                "target_column": "target",
                "feature_columns": FEATURES,
                "steps": [{"step": "standard_scaler"}],
                "model": {"step": model_step},
            }
        ),
    )
    output = train(training_job, df, n_jobs=1, version="test", progress=lambda *_: None)

    storage.put_bytes(dataset_key, df.to_csv(index=False).encode("utf-8"), "text/csv")
    storage.put_bytes(
        join_key(model_prefix, "model.onnx"),
        output.onnx_bytes,
        "application/octet-stream",
    )
    storage.put_json(
        join_key(model_prefix, "metadata.json"), output.metadata.model_dump(mode="json")
    )
    storage.put_json(
        join_key(model_prefix, "split.json"),
        {"train_index": output.train_index, "test_index": output.test_index},
    )

    evaluation_job = EvaluationJob(
        job_id=uuid4(),
        evaluation_id=uuid4(),
        run_id=training_job.run_id,
        experiment_id=training_job.experiment_id,
        dataset_key=dataset_key,
        model_prefix=model_prefix,
        output_prefix="experiments/e/evaluations/v/",
        options=options,
    )
    return evaluation_job, df


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


def test_permutation_and_shap_rank_true_features_top(s3_storage: Storage) -> None:
    options = EvaluationOptions(
        global_methods=["permutation", "shap"],
        permutation_repeats=5,
        num_features_to_plot=6,
    )
    job, _df = _regression_env(s3_storage, options)
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


def test_classification_run_writes_tables_and_plots(s3_storage: Storage) -> None:
    options = EvaluationOptions(
        global_methods=["permutation", "shap"],
        permutation_repeats=5,
        num_features_to_plot=6,
    )
    job, _df = _classification_env(s3_storage, options)
    ctx = load_context(job, s3_storage, n_jobs=1)

    GlobalImportanceStep().run(ctx)

    kinds = {artifact["key"]: artifact["kind"] for artifact in ctx.artifacts}
    assert kinds["experiments/e/evaluations/v/tables/global_permutation.csv"] == "table"
    assert kinds["experiments/e/evaluations/v/tables/global_shap.csv"] == "table"
    assert kinds["experiments/e/evaluations/v/plots/global_permutation.png"] == "plot"
    assert kinds["experiments/e/evaluations/v/plots/global_shap_bar.png"] == "plot"
    assert kinds["experiments/e/evaluations/v/plots/global_shap_beeswarm.png"] == "plot"


def test_shap_only_writes_only_shap_outputs(s3_storage: Storage) -> None:
    options = EvaluationOptions(
        global_methods=["shap"], permutation_repeats=5, num_features_to_plot=6
    )
    job, _df = _regression_env(s3_storage, options)
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
