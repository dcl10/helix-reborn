from typing import Any
from uuid import uuid4

import numpy as np
import pandas as pd
import pytest
from sklearn.datasets import make_classification, make_regression

from helix_core.errors import InvalidDataError, InvalidSpecError
from helix_core.messages import PipelineSpec, SearchSpec, TrainingJob
from helix_core.onnx_export import OnnxParityError, predict_onnx
from helix_training import train as train_module
from helix_training.train import train

N_ROWS = 120
FEATURES = ["f0", "f1", "f2", "f3", "f4"]


def _classification_df(n_classes: int = 2, labels: list[Any] | None = None):
    x, y = make_classification(
        n_samples=N_ROWS,
        n_features=len(FEATURES),
        n_informative=4,
        n_redundant=0,
        n_classes=n_classes,
        random_state=42,
    )
    df = pd.DataFrame(x, columns=FEATURES)
    df["target"] = [labels[i] for i in y] if labels else y
    return df


def _regression_df() -> pd.DataFrame:
    x, y = make_regression(
        n_samples=N_ROWS, n_features=len(FEATURES), noise=0.5, random_state=42
    )
    df = pd.DataFrame(x, columns=FEATURES)
    df["target"] = y
    return df


def _job(
    problem_type: str = "classification",
    model: str = "logistic_regression",
    model_params: dict[str, Any] | None = None,
    steps: list[dict[str, Any]] | None = None,
    split: dict[str, Any] | None = None,
    search: dict[str, Any] | None = None,
    features: list[str] | None = None,
) -> TrainingJob:
    return TrainingJob(
        job_id=uuid4(),
        run_id=uuid4(),
        experiment_id=uuid4(),
        dataset_key="experiments/e/datasets/d/data.csv",
        output_prefix="experiments/e/runs/r/",
        pipeline=PipelineSpec.model_validate(
            {
                "problem_type": problem_type,
                "target_column": "target",
                "feature_columns": features or FEATURES,
                "split": split or {},
                "steps": steps or [],
                "model": {"step": model, "params": model_params or {}},
                "search": search,
            }
        ),
    )


def _train(job: TrainingJob, df: pd.DataFrame):
    calls: list[tuple[float, str]] = []
    output = train(
        job, df, n_jobs=1, version="test", progress=lambda p, m: calls.append((p, m))
    )
    return output, calls


def test_train_binary_string_labels_encodes_classes_and_stratifies() -> None:
    df = _classification_df(labels=["no", "yes"])

    output, _ = _train(_job(steps=[{"step": "standard_scaler"}]), df)

    assert output.metadata.classes == ["no", "yes"]
    assert output.metadata.onnx_outputs == {
        "label": "label",
        "probabilities": "probabilities",
    }
    labels, probabilities = predict_onnx(
        output.onnx_bytes, df[FEATURES].to_numpy(), "classification"
    )
    assert set(labels.tolist()) <= {0, 1}
    assert probabilities.shape == (N_ROWS, 2)

    train_index, test_index = output.train_index, output.test_index
    assert len(test_index) == 24
    assert len(train_index) == 96
    assert not set(train_index) & set(test_index)
    assert sorted(train_index + test_index) == list(range(N_ROWS))

    overall = (df["target"] == "yes").mean()
    for index in (train_index, test_index):
        assert abs((df["target"].iloc[index] == "yes").mean() - overall) <= 0.1


def test_train_multiclass_with_cv_reports_the_five_classification_metrics() -> None:
    df = _classification_df(n_classes=3)

    output, _ = _train(_job(split={"cv_folds": 3}), df)

    assert output.cv_metrics is not None
    assert set(output.cv_metrics) == {
        "accuracy",
        "f1_weighted",
        "precision_weighted",
        "recall_weighted",
        "roc_auc",
    }
    for value in output.cv_metrics.values():
        assert set(value) == {"mean", "std"}
        assert 0 <= value["mean"] <= 1
    assert output.metadata.classes == [0, 1, 2]
    assert output.best_params is None


def test_train_regression_grid_search_picks_from_the_grid_with_positive_errors() -> (
    None
):
    df = _regression_df()

    output, _ = _train(
        _job(
            problem_type="regression",
            model="ridge",
            split={"cv_folds": 3},
            search={
                "method": "grid",
                "cv_folds": 3,
                "param_grid": {"alpha": [0.1, 1.0, 10.0]},
            },
        ),
        df,
    )

    assert output.best_params is not None
    assert output.best_params["alpha"] in [0.1, 1.0, 10.0]
    assert output.cv_metrics is not None
    assert set(output.cv_metrics) == {"r2", "mae", "rmse"}
    assert output.cv_metrics["mae"]["mean"] > 0
    assert output.cv_metrics["rmse"]["mean"] > 0
    assert output.metadata.classes is None
    assert output.metadata.onnx_outputs == {"value": "variable"}


def test_train_random_search_returns_plain_json_params() -> None:
    df = _classification_df()

    output, _ = _train(
        _job(
            model="mlp_classifier",
            model_params={"max_iter": 50},
            search={
                "method": "random",
                "n_iter": 2,
                "cv_folds": 2,
                "param_grid": {"hidden_layer_sizes": [[5], [5, 3]]},
            },
        ),
        df,
    )

    assert output.best_params is not None
    assert output.best_params["hidden_layer_sizes"] in ([5], [5, 3])


def test_train_random_search_without_n_iter_is_an_invalid_spec() -> None:
    job = _job(search={"method": "grid"})
    job.pipeline.search = SearchSpec.model_construct(
        method="random", cv_folds=3, n_iter=None, param_grid=None
    )

    with pytest.raises(InvalidSpecError, match="n_iter"):
        _train(job, _classification_df())


def test_train_excludes_rows_with_no_target_from_both_splits() -> None:
    df = _classification_df()
    df["target"] = df["target"].astype(float)
    missing = [3, 17, 50]
    df.loc[missing, "target"] = np.nan

    output, _ = _train(_job(), df)

    used = set(output.train_index) | set(output.test_index)
    assert not used & set(missing)
    assert len(used) == N_ROWS - len(missing)


def test_train_rejects_a_class_with_one_row() -> None:
    df = _classification_df()
    df.loc[0, "target"] = 7

    with pytest.raises(InvalidDataError, match="these have 1: 7"):
        _train(_job(), df)


def test_train_rejects_a_non_numeric_feature() -> None:
    df = _classification_df()
    df["f0"] = "text"

    with pytest.raises(InvalidDataError, match="Non-numeric"):
        _train(_job(), df)


def test_train_rejects_fewer_than_20_rows() -> None:
    df = _classification_df().iloc[:19]

    with pytest.raises(InvalidDataError, match="at least 20 rows"):
        _train(_job(), df)


def test_train_reports_increasing_progress() -> None:
    df = _regression_df()

    _, calls = _train(
        _job(
            problem_type="regression",
            model="ridge",
            split={"cv_folds": 3},
            search={"method": "grid", "cv_folds": 3},
        ),
        df,
    )

    fractions = [fraction for fraction, _ in calls]
    assert fractions == sorted(fractions)
    assert len(set(fractions)) == len(fractions) == 4


def test_train_adds_the_scaler_hint_to_a_parity_failure_before_a_tree(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    def fail(*_args: Any) -> None:
        raise OnnxParityError("ONNX probabilities differ")

    monkeypatch.setattr(train_module, "check_parity", fail)

    with pytest.raises(OnnxParityError, match="Try removing 'min_max_scaler'"):
        _train(
            _job(
                steps=[{"step": "min_max_scaler"}],
                model="xgboost_classifier",
                model_params={"n_estimators": 10},
            ),
            _classification_df(),
        )


def test_train_reraises_a_parity_failure_unchanged_without_a_redundant_scaler(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    def fail(*_args: Any) -> None:
        raise OnnxParityError("ONNX probabilities differ")

    monkeypatch.setattr(train_module, "check_parity", fail)

    with pytest.raises(OnnxParityError) as excinfo:
        _train(
            _job(model="xgboost_classifier", model_params={"n_estimators": 10}),
            _classification_df(),
        )

    assert str(excinfo.value) == "ONNX probabilities differ"


def test_train_rejects_more_knn_neighbours_than_training_rows() -> None:
    df = _classification_df()

    with pytest.raises(InvalidSpecError, match="at most 96 neighbours"):
        _train(_job(model="knn_classifier", model_params={"n_neighbors": 100}), df)


def test_train_checks_knn_neighbours_against_the_smallest_cv_fold() -> None:
    df = _classification_df()

    # 96 training rows: 4-fold CV fits on 72, the search's 2-fold CV on 48.
    with pytest.raises(InvalidSpecError, match="at most 48 neighbours"):
        _train(
            _job(
                model="knn_classifier",
                split={"cv_folds": 4},
                search={
                    "method": "grid",
                    "cv_folds": 2,
                    "param_grid": {"n_neighbors": [5, 80]},
                },
            ),
            df,
        )


def test_train_reports_an_estimator_rejecting_the_data_as_invalid_data() -> None:
    df = _classification_df()

    with pytest.raises(InvalidDataError, match="Cross-validation failed"):
        # A 2-row class can't fill 3 stratified folds' test sets for ROC AUC.
        df.loc[[0, 1], "target"] = 5
        df["target"] = df["target"].astype(int)
        _train(_job(split={"cv_folds": 10}), df)
