import numpy as np
from sklearn.linear_model import LogisticRegression
from sklearn.pipeline import Pipeline

from helix_core.onnx_export import check_parity, to_onnx
from helix_core.pipelines import CorrelationThreshold


def _correlated_data() -> tuple[np.ndarray, np.ndarray]:
    rng = np.random.default_rng(42)
    a = rng.normal(size=120)
    b = rng.normal(size=120)
    # Column 2 is column 0 doubled: perfectly correlated with it.
    x = np.column_stack([a, b, 2 * a, rng.normal(size=120)])
    y = (a + b > 0).astype(int)
    return x, y


def test_correlation_threshold_drops_one_of_two_perfectly_correlated_columns() -> None:
    x, _ = _correlated_data()

    selector = CorrelationThreshold(threshold=0.9).fit(x)

    assert selector.get_support().tolist() == [True, True, False, True]
    assert selector.transform(x).shape == (120, 3)


def test_correlation_threshold_of_one_keeps_every_column() -> None:
    x, _ = _correlated_data()

    selector = CorrelationThreshold(threshold=1.0).fit(x)

    assert selector.get_support().all()


def test_correlation_threshold_ignores_missing_values_and_keeps_constant_columns() -> (
    None
):
    x, _ = _correlated_data()
    x[::10, 0] = np.nan
    x = np.column_stack([x, np.ones(len(x))])

    selector = CorrelationThreshold(threshold=0.9).fit(x)

    assert selector.get_support().tolist() == [True, True, False, True, True]


def test_correlation_threshold_converts_to_onnx_with_parity() -> None:
    x, y = _correlated_data()
    pipeline = Pipeline(
        [
            ("select", CorrelationThreshold(threshold=0.9)),
            ("model", LogisticRegression()),
        ]
    ).fit(x, y)

    onnx_bytes = to_onnx(pipeline, x.shape[1], "classification")

    check_parity(pipeline, onnx_bytes, x, "classification")
