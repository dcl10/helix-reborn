"""A scikit-learn-compatible adapter over an exported ONNX model.

Evaluation uses the ONNX file, never the pickled scikit-learn object (design rule 4
in ``docs/architecture.md``). :class:`OnnxClassifier` and :class:`OnnxRegressor` wrap
an ``onnxruntime`` session behind the scikit-learn estimator interface, so tools that
expect a fitted estimator (``sklearn.inspection.permutation_importance``, SHAP, LIME)
can use the ONNX model directly.
"""

from typing import Any

import numpy as np
import onnxruntime as ort
from numpy.typing import ArrayLike
from sklearn.base import BaseEstimator, ClassifierMixin, RegressorMixin

from helix_core.model_metadata import ModelMetadata
from helix_core.settings import get_settings


def _as_float32(x: ArrayLike) -> np.ndarray:
    """Convert model input to a ``float32`` array.

    Args:
        x: The input features. A ``pandas.DataFrame`` keeps its column order.

    Returns:
        The input as a ``float32`` numpy array.
    """
    return np.asarray(x, dtype=np.float32)


def _make_session(onnx_bytes: bytes) -> ort.InferenceSession:
    """Create an ONNX Runtime session using the worker's configured CPU count.

    Args:
        onnx_bytes: The serialized ONNX model.

    Returns:
        A session restricted to ``CPUExecutionProvider``.
    """
    options = ort.SessionOptions()
    options.intra_op_num_threads = get_settings().worker_cpus
    return ort.InferenceSession(
        onnx_bytes, sess_options=options, providers=["CPUExecutionProvider"]
    )


class OnnxClassifier(ClassifierMixin, BaseEstimator):
    """A scikit-learn-compatible wrapper over an ONNX classifier.

    The model is already trained; :meth:`fit` is a no-op. ``classes_`` is always
    ``np.arange(n_classes)`` since the ONNX model's label output is the
    label-encoded class index, not the original label (see
    :class:`~helix_core.model_metadata.ModelMetadata`).
    """

    def __init__(
        self,
        onnx_bytes: bytes,
        n_classes: int,
        input_name: str,
        label_output: str,
        proba_output: str,
    ) -> None:
        """Initialize the classifier.

        Args:
            onnx_bytes: The serialized ONNX model.
            n_classes: The number of classes the model predicts.
            input_name: The ONNX model's input tensor name.
            label_output: The ONNX model's label output tensor name.
            proba_output: The ONNX model's probabilities output tensor name.
        """
        self.onnx_bytes = onnx_bytes
        self.n_classes = n_classes
        self.input_name = input_name
        self.label_output = label_output
        self.proba_output = proba_output
        self.classes_ = np.arange(n_classes)

    @property
    def _session(self) -> ort.InferenceSession:
        session = getattr(self, "_session_cache", None)
        if session is None:
            session = _make_session(self.onnx_bytes)
            self._session_cache = session
        return session

    def fit(self, x: Any = None, y: Any = None) -> "OnnxClassifier":
        """Do nothing: the model is already trained.

        Args:
            x: Ignored.
            y: Ignored.

        Returns:
            This instance, unchanged.
        """
        return self

    def predict(self, x: ArrayLike) -> np.ndarray:
        """Predict the class index for each row.

        Args:
            x: The input features.

        Returns:
            The predicted class index for each row.
        """
        (labels,) = self._session.run(
            [self.label_output], {self.input_name: _as_float32(x)}
        )
        return np.asarray(labels)

    def predict_proba(self, x: ArrayLike) -> np.ndarray:
        """Predict class probabilities for each row.

        Args:
            x: The input features.

        Returns:
            An array of shape ``[n_samples, n_classes]``; each row sums to 1.
        """
        (probabilities,) = self._session.run(
            [self.proba_output], {self.input_name: _as_float32(x)}
        )
        return np.asarray(probabilities)

    def __sklearn_is_fitted__(self) -> bool:
        return True


class OnnxRegressor(RegressorMixin, BaseEstimator):
    """A scikit-learn-compatible wrapper over an ONNX regressor.

    The model is already trained; :meth:`fit` is a no-op.
    """

    def __init__(self, onnx_bytes: bytes, input_name: str, value_output: str) -> None:
        """Initialize the regressor.

        Args:
            onnx_bytes: The serialized ONNX model.
            input_name: The ONNX model's input tensor name.
            value_output: The ONNX model's value output tensor name.
        """
        self.onnx_bytes = onnx_bytes
        self.input_name = input_name
        self.value_output = value_output

    @property
    def _session(self) -> ort.InferenceSession:
        session = getattr(self, "_session_cache", None)
        if session is None:
            session = _make_session(self.onnx_bytes)
            self._session_cache = session
        return session

    def fit(self, x: Any = None, y: Any = None) -> "OnnxRegressor":
        """Do nothing: the model is already trained.

        Args:
            x: Ignored.
            y: Ignored.

        Returns:
            This instance, unchanged.
        """
        return self

    def predict(self, x: ArrayLike) -> np.ndarray:
        """Predict the target value for each row.

        Args:
            x: The input features.

        Returns:
            A 1-D array of predicted values.
        """
        (values,) = self._session.run(
            [self.value_output], {self.input_name: _as_float32(x)}
        )
        return np.asarray(values).reshape(-1)

    def __sklearn_is_fitted__(self) -> bool:
        return True


def load_model(
    onnx_bytes: bytes, metadata: ModelMetadata
) -> OnnxClassifier | OnnxRegressor:
    """Build the scikit-learn-compatible adapter for a model's ONNX export.

    Args:
        onnx_bytes: The serialized ONNX model (``model.onnx``).
        metadata: The model's metadata, naming its input and output tensors.

    Returns:
        An :class:`OnnxClassifier` for a classification model, or an
        :class:`OnnxRegressor` for a regression one.
    """
    if metadata.problem_type == "classification":
        return OnnxClassifier(
            onnx_bytes=onnx_bytes,
            n_classes=len(metadata.classes or []),
            input_name=metadata.onnx_input_name,
            label_output=metadata.onnx_outputs["label"],
            proba_output=metadata.onnx_outputs["probabilities"],
        )
    return OnnxRegressor(
        onnx_bytes=onnx_bytes,
        input_name=metadata.onnx_input_name,
        value_output=metadata.onnx_outputs["value"],
    )
