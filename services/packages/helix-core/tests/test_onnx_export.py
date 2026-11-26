import numpy as np
import pytest
from sklearn.base import BaseEstimator, ClassifierMixin
from sklearn.datasets import make_classification, make_regression
from sklearn.ensemble import RandomForestClassifier
from sklearn.impute import SimpleImputer
from sklearn.linear_model import LogisticRegression, Ridge
from sklearn.neural_network import MLPRegressor
from sklearn.pipeline import Pipeline
from sklearn.preprocessing import RobustScaler, StandardScaler
from sklearn.svm import SVC
from xgboost import XGBClassifier, XGBRegressor

from helix_core.onnx_export import (
    OnnxConversionError,
    OnnxOutputs,
    OnnxParityError,
    check_parity,
    inspect_outputs,
    predict_onnx,
    to_onnx,
)

N_SAMPLES = 200


def _classification_data(n_classes: int = 2, n_features: int = 5):
    return make_classification(
        n_samples=N_SAMPLES,
        n_features=n_features,
        n_informative=n_features,
        n_redundant=0,
        n_classes=n_classes,
        n_clusters_per_class=1,
        random_state=42,
    )


def _regression_data(n_features: int = 5):
    return make_regression(n_samples=N_SAMPLES, n_features=n_features, random_state=42)


CLASSIFICATION_PIPELINES = {
    "logistic_regression": lambda: Pipeline(
        [("scale", StandardScaler()), ("model", LogisticRegression())]
    ),
    "random_forest": lambda: Pipeline(
        [
            ("impute", SimpleImputer()),
            ("model", RandomForestClassifier(n_estimators=10, random_state=42)),
        ]
    ),
    # No scaler: float32 rounding in a scaler flips a few rows at XGBoost's split
    # thresholds, beyond check_parity's 1% budget (see docs/pipelines.md).
    "xgboost": lambda: Pipeline(
        [("model", XGBClassifier(n_estimators=10, random_state=42))]
    ),
    "svc": lambda: Pipeline(
        [("scale", StandardScaler()), ("model", SVC(probability=True))]
    ),
}

REGRESSION_PIPELINES = {
    "ridge": lambda: Pipeline([("scale", RobustScaler()), ("model", Ridge())]),
    "xgboost": lambda: Pipeline(
        [("model", XGBRegressor(n_estimators=10, random_state=42))]
    ),
    "mlp": lambda: Pipeline([("model", MLPRegressor(max_iter=200, random_state=42))]),
}


@pytest.mark.parametrize("name", CLASSIFICATION_PIPELINES)
def test_classification_round_trip_and_parity(name: str) -> None:
    x, y = _classification_data()
    pipeline = CLASSIFICATION_PIPELINES[name]().fit(x, y)

    onnx_bytes = to_onnx(pipeline, x.shape[1], "classification")

    check_parity(pipeline, onnx_bytes, x, "classification")


@pytest.mark.parametrize("name", REGRESSION_PIPELINES)
def test_regression_round_trip_and_parity(name: str) -> None:
    x, y = _regression_data()
    pipeline = REGRESSION_PIPELINES[name]().fit(x, y)

    onnx_bytes = to_onnx(pipeline, x.shape[1], "regression")

    check_parity(pipeline, onnx_bytes, x, "regression")


def test_multi_class_probabilities_have_the_right_shape() -> None:
    x, y = _classification_data(n_classes=3, n_features=6)
    pipeline = Pipeline(
        [("scale", StandardScaler()), ("model", LogisticRegression())]
    ).fit(x, y)

    onnx_bytes = to_onnx(pipeline, x.shape[1], "classification")
    _, probabilities = predict_onnx(onnx_bytes, x, "classification")

    assert probabilities.shape == (N_SAMPLES, 3)


def test_inspect_outputs_for_classifier() -> None:
    x, y = _classification_data()
    pipeline = Pipeline(
        [("scale", StandardScaler()), ("model", LogisticRegression())]
    ).fit(x, y)
    onnx_bytes = to_onnx(pipeline, x.shape[1], "classification")

    outputs = inspect_outputs(onnx_bytes, "classification")

    assert outputs == OnnxOutputs(label="label", probabilities="probabilities")


def test_inspect_outputs_for_regressor() -> None:
    x, y = _regression_data()
    pipeline = Pipeline([("scale", RobustScaler()), ("model", Ridge())]).fit(x, y)
    onnx_bytes = to_onnx(pipeline, x.shape[1], "regression")

    outputs = inspect_outputs(onnx_bytes, "regression")

    assert outputs == OnnxOutputs(value="variable")


def test_pipeline_with_simple_imputer_handles_nan_input() -> None:
    x, y = _classification_data()
    rng = np.random.default_rng(42)
    x_with_nan = x.copy()
    x_with_nan[rng.integers(0, N_SAMPLES, size=10), 0] = np.nan
    pipeline = Pipeline(
        [
            ("impute", SimpleImputer()),
            ("model", RandomForestClassifier(n_estimators=10, random_state=42)),
        ]
    ).fit(x_with_nan, y)

    onnx_bytes = to_onnx(pipeline, x_with_nan.shape[1], "classification")

    check_parity(pipeline, onnx_bytes, x_with_nan, "classification")


def test_check_parity_raises_for_a_different_pipelines_onnx() -> None:
    x, y = _classification_data()
    pipeline_a = Pipeline(
        [("scale", StandardScaler()), ("model", LogisticRegression())]
    ).fit(x, y)
    y_b = 1 - y
    pipeline_b = Pipeline(
        [("scale", StandardScaler()), ("model", LogisticRegression())]
    ).fit(x, y_b)
    onnx_bytes_b = to_onnx(pipeline_b, x.shape[1], "classification")

    with pytest.raises(OnnxParityError):
        check_parity(pipeline_a, onnx_bytes_b, x, "classification")


class _UnconvertibleEstimator(BaseEstimator, ClassifierMixin):
    def fit(self, x: np.ndarray, y: np.ndarray) -> "_UnconvertibleEstimator":
        self.classes_ = np.unique(y)
        return self

    def predict(self, x: np.ndarray) -> np.ndarray:
        return np.zeros(len(x), dtype=int)


def test_to_onnx_on_unconvertible_estimator_raises_onnx_conversion_error() -> None:
    x, y = _classification_data()
    pipeline = Pipeline(
        [("scale", StandardScaler()), ("model", _UnconvertibleEstimator())]
    ).fit(x, y)

    with pytest.raises(OnnxConversionError):
        to_onnx(pipeline, x.shape[1], "classification")
