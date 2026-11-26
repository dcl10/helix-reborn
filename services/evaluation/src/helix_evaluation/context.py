"""Everything the evaluation step framework needs to evaluate one job.

:func:`load_context` reads a trained model and its dataset from S3 and builds an
:class:`EvaluationContext`; each :class:`~helix_evaluation.steps.EvaluationStep`
reads and writes it as it runs.
"""

from dataclasses import dataclass, field
from typing import Any

import numpy as np
import pandas as pd
from matplotlib.figure import Figure

from helix_core.datasets import load_csv, select_features_target
from helix_core.errors import InvalidDataError
from helix_core.messages import EvaluationJob, PlotOptions
from helix_core.model_metadata import ModelMetadata
from helix_core.plotting import save_figure
from helix_core.storage import Storage, join_key
from helix_evaluation.model import OnnxClassifier, OnnxRegressor, load_model


def _encode_classes(y: pd.Series, classes: list[str | int | float]) -> np.ndarray:
    """Map each target value to its index in ``classes``.

    Args:
        y: The raw target values.
        classes: The original class labels, in index order, as stored in
            :class:`~helix_core.model_metadata.ModelMetadata`.

    Returns:
        The class index of each value, as an ``int64`` array.

    Raises:
        InvalidDataError: If a value isn't one of ``classes``.
    """
    as_str = all(isinstance(label, str) for label in classes)
    mapping = {
        (str(label) if as_str else label): index for index, label in enumerate(classes)
    }
    keys = y.astype(str) if as_str else y

    indices = []
    for value in keys:
        if value not in mapping:
            raise InvalidDataError(
                f"Target value {value!r} isn't one of the model's classes: {classes}"
            )
        indices.append(mapping[value])
    return np.array(indices, dtype=np.int64)


@dataclass
class EvaluationContext:
    """Everything an :class:`~helix_evaluation.steps.EvaluationStep` reads and writes.

    Attributes:
        job: The evaluation job being run.
        metadata: The model's ``metadata.json``.
        model: The ONNX model, wrapped as a scikit-learn-compatible estimator.
        x_train: The training rows' feature columns, in ``metadata.feature_columns``
            order.
        y_train: The training rows' target values; label-encoded to class indices
            for classification.
        x_test: The test rows' feature columns.
        y_test: The test rows' target values; label-encoded to class indices for
            classification.
        storage: The S3 storage to write plots, tables and JSON outputs to.
        n_jobs: Cores available to steps that parallelise (for example permutation
            importance).
        plot_options: Shared plot styling options.
        artifacts: Every artifact written so far: ``{"key", "kind", "title"}``.
        results: Results steps share with later steps, keyed by step name (for
            example ``"metrics"``, ``"global_importance"``, ``"local_importance"``).
    """

    job: EvaluationJob
    metadata: ModelMetadata
    model: OnnxClassifier | OnnxRegressor
    x_train: pd.DataFrame
    y_train: np.ndarray
    x_test: pd.DataFrame
    y_test: np.ndarray
    storage: Storage
    n_jobs: int
    plot_options: PlotOptions
    artifacts: list[dict[str, str]] = field(default_factory=list)
    results: dict[str, Any] = field(default_factory=dict)

    def save_plot(self, name: str, fig: Figure, title: str) -> str:
        """Render a figure to PNG, upload it and record it as an artifact.

        Args:
            name: The plot's file name, without extension.
            fig: The figure to render. Closed as a side effect.
            title: A human-readable title for the artifact.

        Returns:
            The S3 key the plot was written to.
        """
        key = join_key(self.job.output_prefix, "plots", f"{name}.png")
        save_figure(self.storage, key, fig, self.plot_options.dpi)
        self.artifacts.append({"key": key, "kind": "plot", "title": title})
        return key

    def save_table(self, name: str, df: pd.DataFrame, title: str) -> str:
        """Write a table to CSV, upload it and record it as an artifact.

        Args:
            name: The table's file name, without extension.
            df: The table to write, with its index included.
            title: A human-readable title for the artifact.

        Returns:
            The S3 key the table was written to.
        """
        key = join_key(self.job.output_prefix, "tables", f"{name}.csv")
        self.storage.put_bytes(key, df.to_csv(index=True).encode("utf-8"), "text/csv")
        self.artifacts.append({"key": key, "kind": "table", "title": title})
        return key

    def save_json(self, name: str, value: object, title: str) -> str:
        """Write a JSON-serializable value, upload it and record it as an artifact.

        Args:
            name: The output's file name, without extension.
            value: The value to serialize as JSON.
            title: A human-readable title for the artifact.

        Returns:
            The S3 key the value was written to.
        """
        key = join_key(self.job.output_prefix, f"{name}.json")
        self.storage.put_json(key, value)
        self.artifacts.append({"key": key, "kind": "json", "title": title})
        return key


def load_context(
    job: EvaluationJob, storage: Storage, n_jobs: int
) -> EvaluationContext:
    """Load everything an evaluation job needs from S3.

    Reads ``metadata.json``, ``model.onnx`` and ``split.json`` from
    ``job.model_prefix``, and the dataset from ``job.dataset_key``. Rows are
    selected by the 0-based positions in ``split.json`` (``df.iloc[...]``).

    Args:
        job: The evaluation job.
        storage: The S3 storage to read from.
        n_jobs: Cores available to steps that parallelise.

    Returns:
        The loaded :class:`EvaluationContext`, ready for
        :func:`~helix_evaluation.steps.run_steps`.

    Raises:
        StorageError: If an expected object is missing or unreadable.
        InvalidDataError: If the dataset doesn't match the model's metadata, or a
            target value isn't one of the model's classes.
    """
    metadata = ModelMetadata.model_validate(
        storage.get_json(join_key(job.model_prefix, "metadata.json"))
    )
    onnx_bytes = storage.get_bytes(join_key(job.model_prefix, "model.onnx"))
    split = storage.get_json(join_key(job.model_prefix, "split.json"))
    df = load_csv(storage, job.dataset_key)

    x_df, y, _ = select_features_target(
        df, metadata.feature_columns, metadata.target_column, metadata.id_column
    )
    assert y is not None

    train_positions = split["train_index"]
    test_positions = split["test_index"]
    x_train = x_df.iloc[train_positions].reset_index(drop=True)
    x_test = x_df.iloc[test_positions].reset_index(drop=True)

    if metadata.problem_type == "classification":
        classes = metadata.classes or []
        y_train = _encode_classes(y.iloc[train_positions], classes)
        y_test = _encode_classes(y.iloc[test_positions], classes)
    else:
        y_train = y.iloc[train_positions].to_numpy(dtype=np.float64)
        y_test = y.iloc[test_positions].to_numpy(dtype=np.float64)

    model = load_model(onnx_bytes, metadata)

    return EvaluationContext(
        job=job,
        metadata=metadata,
        model=model,
        x_train=x_train,
        y_train=y_train,
        x_test=x_test,
        y_test=y_test,
        storage=storage,
        n_jobs=n_jobs,
        plot_options=job.options.plot_options or PlotOptions(),
    )
