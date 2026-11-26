import numpy as np
from sklearn.base import clone
from sklearn.inspection import permutation_importance

from helix_evaluation.context import load_context
from helix_evaluation.model import load_model


def test_onnx_classifier_predict_proba_rows_sum_to_one(classification_env) -> None:
    _storage, _job, df, output = classification_env
    model = load_model(output.onnx_bytes, output.metadata)
    x = df[["f0", "f1", "f2", "f3"]].to_numpy(dtype=np.float32)

    proba = model.predict_proba(x)

    assert np.allclose(proba.sum(axis=1), 1.0, atol=1e-5)


def test_onnx_classifier_predict_matches_argmax(classification_env) -> None:
    _storage, _job, df, output = classification_env
    model = load_model(output.onnx_bytes, output.metadata)
    x = df[["f0", "f1", "f2", "f3"]].to_numpy(dtype=np.float32)

    predicted = model.predict(x)
    proba = model.predict_proba(x)

    assert np.array_equal(predicted, np.argmax(proba, axis=1))


def test_onnx_classifier_can_be_cloned(classification_env) -> None:
    _storage, _job, _df, output = classification_env
    model = load_model(output.onnx_bytes, output.metadata)

    cloned = clone(model)

    assert cloned.onnx_bytes == model.onnx_bytes
    assert cloned.n_classes == model.n_classes


def test_onnx_regressor_can_be_cloned(regression_env) -> None:
    _storage, _job, _df, output = regression_env
    model = load_model(output.onnx_bytes, output.metadata)

    cloned = clone(model)

    assert cloned.onnx_bytes == model.onnx_bytes
    assert cloned.value_output == model.value_output


def test_permutation_importance_runs_for_classifier(classification_env) -> None:
    storage, job, _df, _output = classification_env
    ctx = load_context(job, storage, n_jobs=1)

    result = permutation_importance(
        ctx.model, ctx.x_test, ctx.y_test, n_repeats=2, random_state=0
    )

    assert len(result.importances_mean) == ctx.x_test.shape[1]


def test_permutation_importance_runs_for_regressor(regression_env) -> None:
    storage, job, _df, _output = regression_env
    ctx = load_context(job, storage, n_jobs=1)

    result = permutation_importance(
        ctx.model, ctx.x_test, ctx.y_test, n_repeats=2, random_state=0
    )

    assert len(result.importances_mean) == ctx.x_test.shape[1]
