"""ONNX export, parity checking and output inspection for fitted pipelines.

Every model Helix trains is a fitted scikit-learn ``Pipeline`` (preprocessing +
model), exported to ONNX so it can be reused from any language, as documented in
``docs/architecture.md`` ("Model artifacts"). This module converts a fitted pipeline,
checks that the ONNX model predicts the same as scikit-learn, and reports the real
output names so they can be stored in ``metadata.json`` (see
:mod:`helix_core.model_metadata`).
"""

from dataclasses import dataclass

import numpy as np
import onnxruntime as ort
from skl2onnx import convert_sklearn, update_registered_converter
from skl2onnx.common.data_types import FloatTensorType
from skl2onnx.common.shape_calculator import (
    calculate_linear_classifier_output_shapes,
    calculate_linear_regressor_output_shapes,
)
from sklearn.pipeline import Pipeline

from helix_core.errors import HelixError
from helix_core.messages import ProblemType

TARGET_OPSET = 17
"""ONNX opset target for exported models (``ai.onnx.ml`` pinned to 3)."""

_ROW_DISAGREEMENT_TOLERANCE = 0.01
"""Share of rows allowed to disagree: on the label, or on probabilities beyond
``_PROBA_ATOL``. ONNX runs in float32, so a preprocessing step can round a value
differently from scikit-learn and send a row down the other side of a tree split;
that changes the row's probabilities sharply. A conversion bug affects most rows."""
_PROBA_ATOL = 1e-3
_REGRESSION_ATOL = 1e-3

_converters_registered = False


class OnnxConversionError(HelixError):
    """A fitted pipeline could not be converted to ONNX."""


class OnnxParityError(HelixError):
    """The ONNX model's predictions don't match scikit-learn's closely enough."""


def register_converters() -> None:
    """Idempotently register extra skl2onnx converters not built in.

    Registers ``XGBClassifier``/``XGBRegressor`` via the onnxmltools XGBoost converter,
    reusing skl2onnx's linear classifier/regressor shape calculators. #22 adds the
    custom ``CorrelationThreshold`` converter here too.
    """
    global _converters_registered
    if _converters_registered:
        return

    from onnxmltools.convert.xgboost.operator_converters.XGBoost import (
        convert_xgboost,
    )
    from xgboost import XGBClassifier, XGBRegressor

    update_registered_converter(
        XGBClassifier,
        "XGBoostXGBClassifier",
        calculate_linear_classifier_output_shapes,
        convert_xgboost,
        options={"nocl": [True, False], "zipmap": [True, False, "columns"]},
    )
    update_registered_converter(
        XGBRegressor,
        "XGBoostXGBRegressor",
        calculate_linear_regressor_output_shapes,
        convert_xgboost,
    )
    _converters_registered = True


def to_onnx(pipeline: Pipeline, n_features: int, problem_type: ProblemType) -> bytes:
    """Convert a fitted pipeline to an ONNX model.

    Args:
        pipeline: The fitted scikit-learn pipeline to convert.
        n_features: Number of input features (the ``input`` tensor's second
            dimension).
        problem_type: Whether the pipeline is a classifier or a regressor. For
            classification, the final estimator is converted with ``zipmap=False``
            so probabilities come out as a plain float tensor rather than a list of
            dicts.

    Returns:
        The serialized ONNX model.

    Raises:
        OnnxConversionError: If skl2onnx can't convert the pipeline, for example
            because a step has no registered converter.
    """
    register_converters()
    options = None
    if problem_type == "classification":
        final_estimator = pipeline.steps[-1][1]
        options = {id(final_estimator): {"zipmap": False}}
    try:
        onnx_model = convert_sklearn(
            pipeline,
            initial_types=[("input", FloatTensorType([None, n_features]))],
            options=options,
            target_opset={"": TARGET_OPSET, "ai.onnx.ml": 3},
        )
    except Exception as exc:
        raise OnnxConversionError(f"Could not convert pipeline to ONNX: {exc}") from exc
    return onnx_model.SerializeToString()


@dataclass(frozen=True)
class OnnxOutputs:
    """The real output names of a converted ONNX model, as read from its session."""

    label: str | None = None
    probabilities: str | None = None
    value: str | None = None


def inspect_outputs(onnx_bytes: bytes, problem_type: ProblemType) -> OnnxOutputs:
    """Map an ONNX model's output names to their role.

    Args:
        onnx_bytes: The serialized ONNX model.
        problem_type: Whether the model is a classifier or a regressor.

    Returns:
        The output names, in :class:`OnnxOutputs`'s ``label``/``probabilities``
        fields for a classifier, or its ``value`` field for a regressor.
    """
    session = ort.InferenceSession(onnx_bytes, providers=["CPUExecutionProvider"])
    names = [output.name for output in session.get_outputs()]
    if problem_type == "classification":
        label = names[0]
        probabilities = names[1] if len(names) > 1 else None
        return OnnxOutputs(label=label, probabilities=probabilities)
    return OnnxOutputs(value=names[0])


def predict_onnx(
    onnx_bytes: bytes, x: np.ndarray, problem_type: ProblemType
) -> tuple[np.ndarray, np.ndarray | None]:
    """Run an ONNX model on an input array.

    Args:
        onnx_bytes: The serialized ONNX model.
        x: The input features. Cast to ``float32`` before being passed to the
            ONNX runtime.
        problem_type: Whether the model is a classifier or a regressor.

    Returns:
        For classification, a tuple ``(labels, probabilities)`` where ``labels`` is
        an ``int64`` array of shape ``[n]`` and ``probabilities`` is a ``float32``
        array of shape ``[n, k]`` (or ``None`` if the model has no probabilities
        output). For regression, a tuple ``(values, None)`` where ``values`` is a
        ``float`` array of shape ``[n]``.
    """
    session = ort.InferenceSession(onnx_bytes, providers=["CPUExecutionProvider"])
    outputs = inspect_outputs(onnx_bytes, problem_type)
    results = session.run(None, {"input": x.astype(np.float32)})
    output_names = [output.name for output in session.get_outputs()]
    by_name = dict(zip(output_names, results, strict=True))

    if problem_type == "classification":
        labels = by_name[outputs.label]
        probabilities = (
            by_name[outputs.probabilities] if outputs.probabilities else None
        )
        return labels, probabilities

    values = by_name[outputs.value]
    return values.reshape(-1), None


def check_parity(
    pipeline: Pipeline, onnx_bytes: bytes, x: np.ndarray, problem_type: ProblemType
) -> None:
    """Check that an ONNX model's predictions match the fitted pipeline's.

    Both sides are compared on ``x`` cast to ``float32``: scikit-learn trained in
    float64, but tree thresholds can flip right at a boundary, so both sides get
    the same precision input. Even then a preprocessing step can round differently
    in ONNX's float32 arithmetic and flip a split for a few rows, so up to 1% of
    rows may disagree.

    Args:
        pipeline: The fitted scikit-learn pipeline the ONNX model was converted
            from.
        onnx_bytes: The serialized ONNX model to check.
        x: The input features to compare predictions on.
        problem_type: Whether the pipeline is a classifier or a regressor.

    Raises:
        OnnxParityError: For classification, if more than 1% of rows disagree on
            the predicted label, or (when the pipeline exposes ``predict_proba``)
            more than 1% of rows have a probability differing by more than
            ``1e-3``. For regression, if the maximum absolute difference exceeds
            ``1e-3 * max(1.0, std(sklearn predictions))``. The message names the
            worst row.
    """
    x32 = x.astype(np.float32)
    onnx_labels_or_values, onnx_probabilities = predict_onnx(
        onnx_bytes, x32, problem_type
    )

    if problem_type == "classification":
        sklearn_labels = np.asarray(pipeline.predict(x32))
        disagreeing = sklearn_labels != onnx_labels_or_values
        if disagreeing.mean() > _ROW_DISAGREEMENT_TOLERANCE:
            worst = int(np.argmax(disagreeing))
            raise OnnxParityError(
                "ONNX labels disagree with scikit-learn on "
                f"{disagreeing.mean() * 100:.2f}% of rows "
                f"(allowed: {_ROW_DISAGREEMENT_TOLERANCE * 100:.0f}%); e.g. row "
                f"{worst}: scikit-learn {sklearn_labels[worst]!r}, ONNX "
                f"{onnx_labels_or_values[worst]!r}"
            )
        if hasattr(pipeline, "predict_proba") and onnx_probabilities is not None:
            sklearn_probabilities = np.asarray(pipeline.predict_proba(x32))
            row_diff = np.max(np.abs(sklearn_probabilities - onnx_probabilities), 1)
            over = row_diff > _PROBA_ATOL
            if over.mean() > _ROW_DISAGREEMENT_TOLERANCE:
                worst = int(np.argmax(row_diff))
                raise OnnxParityError(
                    "ONNX probabilities differ from scikit-learn by more than "
                    f"{_PROBA_ATOL} on {over.mean() * 100:.2f}% of rows (allowed: "
                    f"{_ROW_DISAGREEMENT_TOLERANCE * 100:.0f}%); worst is row "
                    f"{worst}, off by {row_diff[worst]:.6f}"
                )
        return

    sklearn_values = np.asarray(pipeline.predict(x32)).reshape(-1)
    tolerance = _REGRESSION_ATOL * max(1.0, float(np.std(sklearn_values)))
    diff = np.abs(sklearn_values - onnx_labels_or_values)
    worst = int(np.argmax(diff))
    if diff[worst] > tolerance:
        raise OnnxParityError(
            f"ONNX predictions differ from scikit-learn by up to {diff[worst]:.6f} "
            f"(allowed: {tolerance:.6f}), on row {worst}"
        )
