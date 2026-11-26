"""Train the pipeline a :class:`~helix_core.messages.TrainingJob` describes.

Pure logic with no AMQP or S3, so it can be tested on its own: :func:`train` takes the
job and the loaded dataset and returns the ONNX model, its metadata, the split and
the cross-validation results. The algorithm is documented in ``docs/pipelines.md``
("Training").
"""

import logging
from collections.abc import Callable, Iterator
from contextlib import contextmanager
from dataclasses import dataclass
from datetime import UTC, datetime
from typing import Any

import numpy as np
import pandas as pd
from pydantic import ValidationError
from sklearn.base import clone
from sklearn.model_selection import (
    GridSearchCV,
    KFold,
    RandomizedSearchCV,
    StratifiedKFold,
    cross_validate,
    train_test_split,
)
from sklearn.pipeline import Pipeline
from sklearn.preprocessing import LabelEncoder

from helix_core.datasets import select_features_target
from helix_core.errors import InvalidDataError, InvalidSpecError
from helix_core.messages import PipelineSpec, ProblemType, TrainingJob
from helix_core.model_metadata import ModelMetadata, library_versions
from helix_core.onnx_export import (
    OnnxParityError,
    check_parity,
    inspect_outputs,
    to_onnx,
)
from helix_core.pipelines import (
    build_pipeline,
    redundant_scaler_steps,
    search_space,
    validate_spec,
)

logger = logging.getLogger(__name__)

MIN_ROWS = 20
"""Fewest rows with a target value a dataset needs to be trained on."""

_REGRESSION_SCORERS = {
    "r2": "r2",
    "mae": "neg_mean_absolute_error",
    "rmse": "neg_root_mean_squared_error",
}

Progress = Callable[[float, str], None]
"""Reports training progress: ``progress(fraction, message)``."""


@dataclass
class TrainingOutput:
    """Everything a training run produces.

    Attributes:
        onnx_bytes: The fitted pipeline, exported to ONNX.
        metadata: The model's ``metadata.json``.
        train_index: 0-based positions in the uploaded CSV of the training rows.
        test_index: 0-based positions in the uploaded CSV of the test rows.
        cv_metrics: ``{metric: {"mean", "std"}}`` from cross-validation on the
            training rows, or ``None`` when ``split.cv_folds`` isn't set.
        best_params: The search's best model parameters (unprefixed), or ``None``
            when there was no search.
    """

    onnx_bytes: bytes
    metadata: ModelMetadata
    train_index: list[int]
    test_index: list[int]
    cv_metrics: dict[str, dict[str, float]] | None
    best_params: dict[str, Any] | None


def scorers(problem_type: ProblemType, n_classes: int) -> dict[str, str]:
    """Name the metrics reported for cross-validation.

    Args:
        problem_type: Classification or regression.
        n_classes: The number of classes, for classification.

    Returns:
        Metric name to scikit-learn scorer. Regression's ``mae`` and ``rmse`` use
        scikit-learn's negated scorers; :func:`train` flips them back to positive.
    """
    if problem_type == "regression":
        return dict(_REGRESSION_SCORERS)
    return {
        "accuracy": "accuracy",
        "f1_weighted": "f1_weighted",
        "precision_weighted": "precision_weighted",
        "recall_weighted": "recall_weighted",
        "roc_auc": "roc_auc" if n_classes == 2 else "roc_auc_ovr_weighted",
    }


@contextmanager
def _user_fixable(stage: str) -> Iterator[None]:
    """Report scikit-learn's ``ValueError``s during fitting as bad data.

    They come from the data or spec meeting the estimator (more neighbours than
    rows, a fold missing a class), so the user can fix them.
    """
    try:
        yield
    except ValueError as error:
        raise InvalidDataError(f"{stage} failed: {error}") from error


def _encode_target(
    y: pd.Series, problem_type: ProblemType
) -> tuple[np.ndarray, list[str | int | float] | None]:
    if problem_type == "regression":
        try:
            return pd.to_numeric(y, errors="raise").to_numpy(dtype=np.float64), None
        except (ValueError, TypeError) as error:
            raise InvalidDataError(
                f"Target column '{y.name}' must be numeric for regression."
            ) from error

    values = y if pd.api.types.is_numeric_dtype(y) else y.astype(str)
    encoder = LabelEncoder().fit(values)
    classes = encoder.classes_.tolist()
    if len(classes) < 2:
        raise InvalidDataError(
            f"Target column '{y.name}' needs at least 2 classes; it has {len(classes)}."
        )
    counts = pd.Series(values).value_counts()
    too_few = [str(label) for label in classes if counts[label] < 2]
    if too_few:
        raise InvalidDataError(
            f"Every class needs at least 2 rows; these have 1: {', '.join(too_few)}."
        )
    return encoder.transform(values), classes


def _cv_splitter(
    problem_type: ProblemType, folds: int, seed: int
) -> StratifiedKFold | KFold:
    splitter = StratifiedKFold if problem_type == "classification" else KFold
    return splitter(n_splits=folds, shuffle=True, random_state=seed)


def _check_neighbours(spec: PipelineSpec, n_train: int) -> None:
    """Check k-NN's ``n_neighbors`` fits the fewest rows the model is fitted on.

    scikit-learn only notices too many neighbours at prediction time, and ONNX
    Runtime fails with an internal error, so check before fitting.
    """
    if spec.model.step not in ("knn_classifier", "knn_regressor"):
        return
    fold_counts = [
        folds
        for folds in (
            spec.search.cv_folds if spec.search is not None else None,
            spec.split.cv_folds,
        )
        if folds is not None
    ]
    fewest_rows = min(
        [n_train, *(n_train - -(-n_train // folds) for folds in fold_counts)]
    )
    candidates = [spec.model.params.get("n_neighbors", 5)]
    if spec.search is not None:
        candidates = search_space(spec).get("model__n_neighbors", candidates)
    largest = max(candidates)
    if largest > fewest_rows:
        raise InvalidSpecError(
            f"k-nearest neighbours can use at most {fewest_rows} neighbours here (the "
            f"fewest training rows the model is fitted on), but asks for {largest}."
        )


def _jsonable(value: Any) -> Any:
    if isinstance(value, tuple):
        return list(value)
    if isinstance(value, np.generic):
        return value.item()
    return value


def train(
    job: TrainingJob,
    df: pd.DataFrame,
    n_jobs: int,
    version: str,
    progress: Progress,
) -> TrainingOutput:
    """Train a job's pipeline on its dataset and export it to ONNX.

    Args:
        job: The training job, with its pipeline spec.
        df: The uploaded dataset, rows in CSV order.
        n_jobs: Cores to use. A search or cross-validation parallelises over
            folds and the model gets one core; otherwise the model gets them all.
        version: The Helix version, stored in the metadata.
        progress: Called with increasing fractions as training goes on.

    Returns:
        The ONNX model, its metadata, the split, and cross-validation and search
        results.

    Raises:
        InvalidSpecError: If the pipeline spec is invalid.
        InvalidDataError: If the dataset doesn't fit the spec: missing or
            non-numeric columns, fewer than 20 rows with a target, fewer than 2
            classes or a class with one row, or the estimator rejects the data.
        OnnxParityError: If the ONNX model doesn't match scikit-learn closely
            enough. When a scaler comes before a tree model, the message suggests
            removing it.
    """
    try:
        # The message model checks this when a job is parsed; re-check in case the
        # job was built another way.
        spec = PipelineSpec.model_validate(job.pipeline.model_dump())
    except ValidationError as error:
        raise InvalidSpecError(f"Invalid pipeline spec: {error}") from error
    validate_spec(spec)
    problem_type = spec.problem_type
    seed = spec.split.random_seed

    x_df, y, _ = select_features_target(
        df, spec.feature_columns, spec.target_column, spec.id_column
    )
    has_target = y.notna().to_numpy()
    positions = np.flatnonzero(has_target)
    if len(positions) < MIN_ROWS:
        raise InvalidDataError(
            f"Training needs at least {MIN_ROWS} rows with a value in "
            f"'{spec.target_column}'; the dataset has {len(positions)}."
        )
    x = x_df.to_numpy(dtype=np.float64)[positions]
    y_values, classes = _encode_target(y.iloc[positions], problem_type)

    with _user_fixable("Splitting the data"):
        train_rows, test_rows = train_test_split(
            np.arange(len(positions)),
            test_size=spec.split.test_size,
            random_state=seed,
            shuffle=True,
            stratify=y_values if problem_type == "classification" else None,
        )
    x_train, y_train = x[train_rows], y_values[train_rows]
    x_test = x[test_rows]
    _check_neighbours(spec, len(train_rows))
    progress(0.1, "Split data")

    n_features = len(spec.feature_columns)
    metrics = scorers(problem_type, len(classes or []))
    refit_metric = "f1_weighted" if problem_type == "classification" else "r2"
    pipeline = build_pipeline(spec, n_features, n_jobs)

    best_params: dict[str, Any] | None = None
    estimator: Pipeline = pipeline
    if spec.search is not None:
        cv = _cv_splitter(problem_type, spec.search.cv_folds, seed)
        if spec.search.method == "grid":
            search = GridSearchCV(
                pipeline,
                search_space(spec),
                scoring=metrics[refit_metric],
                cv=cv,
                n_jobs=n_jobs,
                error_score="raise",
            )
        else:
            search = RandomizedSearchCV(
                pipeline,
                search_space(spec),
                n_iter=spec.search.n_iter,
                scoring=metrics[refit_metric],
                cv=cv,
                n_jobs=n_jobs,
                random_state=seed,
                error_score="raise",
            )
        with _user_fixable("Hyperparameter search"):
            search.fit(x_train, y_train)
        estimator = search.best_estimator_
        best_params = {
            key.removeprefix("model__"): _jsonable(value)
            for key, value in search.best_params_.items()
        }
        progress(0.5, "Hyperparameter search done")

    cv_metrics: dict[str, dict[str, float]] | None = None
    if spec.split.cv_folds is not None:
        cv_estimator = clone(estimator)
        # cross_validate parallelises over folds, so the model mustn't as well.
        if "model__n_jobs" in cv_estimator.get_params():
            cv_estimator.set_params(model__n_jobs=1)
        with _user_fixable("Cross-validation"):
            results = cross_validate(
                cv_estimator,
                x_train,
                y_train,
                cv=_cv_splitter(problem_type, spec.split.cv_folds, seed),
                scoring=metrics,
                n_jobs=n_jobs,
                error_score="raise",
            )
        cv_metrics = {}
        for name, scorer in metrics.items():
            scores = results[f"test_{name}"]
            if scorer.startswith("neg_"):
                scores = -scores
            cv_metrics[name] = {
                "mean": float(scores.mean()),
                "std": float(scores.std()),
            }
        progress(0.7, f"Cross-validated ({spec.split.cv_folds} folds)")

    if spec.search is None:
        with _user_fixable("Fitting the model"):
            estimator.fit(x_train, y_train)

    onnx_bytes = to_onnx(estimator, n_features, problem_type)
    try:
        check_parity(estimator, onnx_bytes, x_test, problem_type)
    except OnnxParityError as error:
        redundant = redundant_scaler_steps(spec)
        if not redundant:
            raise
        names = ", ".join(f"'{step}'" for step in redundant)
        raise OnnxParityError(
            f"{error}. Tree models (random forest, XGBoost) don't need scaling, and a "
            "scaler before one can make the exported model differ slightly from the "
            f"trained one. Try removing {names}."
        ) from error
    progress(0.85, "Exported to ONNX")

    outputs = inspect_outputs(onnx_bytes, problem_type)
    onnx_outputs = {
        role: name
        for role, name in (
            ("label", outputs.label),
            ("probabilities", outputs.probabilities),
            ("value", outputs.value),
        )
        if name is not None
    }
    metadata = ModelMetadata(
        run_id=job.run_id,
        problem_type=problem_type,
        target_column=spec.target_column,
        feature_columns=spec.feature_columns,
        id_column=spec.id_column,
        classes=classes,
        onnx_outputs=onnx_outputs,
        pipeline=spec,
        library_versions=library_versions(),
        helix_version=version,
        created_at=datetime.now(UTC),
    )

    return TrainingOutput(
        onnx_bytes=onnx_bytes,
        metadata=metadata,
        train_index=positions[train_rows].tolist(),
        test_index=positions[test_rows].tolist(),
        cv_metrics=cv_metrics,
        best_params=best_params,
    )
