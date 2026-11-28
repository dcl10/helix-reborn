"""Generates the ONNX model fixtures the API's prediction tests run against.

Builds a tiny classification pipeline (``standard_scaler`` + ``logistic_regression``)
and a tiny regression pipeline (``simple_imputer`` + ``ridge``) with helix-core's own
pipeline builder and ONNX exporter -- the same path the training worker uses -- and
writes ``model.onnx``, ``metadata.json`` and ``expected.json`` (the ONNX model's own
predictions for 3 fixed rows, so the C# tests can assert exact parity) to each
fixture folder.

Run from ``services/``::

    uv run python packages/helix-core/scripts/make_test_models.py \\
        --out ../api/tests/HelixReborn.Api.Tests/Fixtures/models
"""

import argparse
import json
from datetime import UTC, datetime
from pathlib import Path
from uuid import uuid4

import numpy as np
from sklearn.preprocessing import LabelEncoder

from helix_core.messages import PipelineSpec
from helix_core.model_metadata import ModelMetadata, library_versions
from helix_core.onnx_export import inspect_outputs, predict_onnx, to_onnx
from helix_core.pipelines import build_pipeline

SEED = 42
N_SAMPLES = 60
N_FEATURES = 3


def _write_fixture(
    folder: Path, onnx_bytes: bytes, metadata: ModelMetadata, expected: dict
) -> None:
    folder.mkdir(parents=True, exist_ok=True)
    (folder / "model.onnx").write_bytes(onnx_bytes)
    (folder / "metadata.json").write_text(metadata.model_dump_json(indent=2) + "\n")
    (folder / "expected.json").write_text(json.dumps(expected, indent=2) + "\n")


def _make_classification_fixture(out_dir: Path) -> None:
    rng = np.random.default_rng(SEED)
    x = rng.normal(size=(N_SAMPLES, N_FEATURES))
    score = 1.5 * x[:, 0] - 2.0 * x[:, 1] + 0.5 * x[:, 2]
    y = np.where(score > 0, "yes", "no")

    encoder = LabelEncoder().fit(y)
    y_encoded = encoder.transform(y)
    classes = encoder.classes_.tolist()

    spec = PipelineSpec.model_validate(
        {
            "problem_type": "classification",
            "target_column": "label",
            "feature_columns": ["f0", "f1", "f2"],
            "id_column": "sample_id",
            "steps": [{"step": "standard_scaler"}],
            "model": {"step": "logistic_regression", "params": {}},
        }
    )
    pipeline = build_pipeline(spec, n_features=N_FEATURES, n_jobs=1)
    pipeline.fit(x, y_encoded)

    onnx_bytes = to_onnx(pipeline, n_features=N_FEATURES, problem_type="classification")
    outputs = inspect_outputs(onnx_bytes, "classification")

    rows = np.array(
        [[0.5, -1.0, 0.2], [-0.3, 0.8, -0.5], [1.2, 0.1, -0.9]], dtype=np.float32
    )
    labels, probabilities = predict_onnx(onnx_bytes, rows, "classification")

    metadata = ModelMetadata(
        run_id=uuid4(),
        problem_type="classification",
        target_column="label",
        feature_columns=["f0", "f1", "f2"],
        id_column="sample_id",
        classes=classes,
        onnx_outputs={"label": outputs.label, "probabilities": outputs.probabilities},
        pipeline=spec,
        library_versions=library_versions(),
        helix_version="dev",
        created_at=datetime.now(UTC),
    )
    expected = {
        "ids": ["S1", "S2", "S3"],
        "rows": rows.tolist(),
        "labels": [classes[i] for i in labels.tolist()],
        "probabilities": probabilities.tolist(),
    }
    _write_fixture(out_dir / "classification", onnx_bytes, metadata, expected)


def _make_regression_fixture(out_dir: Path) -> None:
    rng = np.random.default_rng(SEED)
    x = rng.normal(size=(N_SAMPLES, N_FEATURES))
    y = (
        2.0 * x[:, 0]
        - 1.0 * x[:, 1]
        + 0.3 * x[:, 2]
        + rng.normal(scale=0.1, size=N_SAMPLES)
    )
    # A couple of missing values, so the fitted imputer has a statistic to fill
    # blanks with at prediction time.
    x[0, 1] = np.nan
    x[5, 2] = np.nan

    spec = PipelineSpec.model_validate(
        {
            "problem_type": "regression",
            "target_column": "y",
            "feature_columns": ["f0", "f1", "f2"],
            "steps": [{"step": "simple_imputer", "params": {"strategy": "median"}}],
            "model": {"step": "ridge", "params": {"alpha": 1.0}},
        }
    )
    pipeline = build_pipeline(spec, n_features=N_FEATURES, n_jobs=1)
    pipeline.fit(x, y)

    onnx_bytes = to_onnx(pipeline, n_features=N_FEATURES, problem_type="regression")
    outputs = inspect_outputs(onnx_bytes, "regression")

    rows = np.array(
        [[0.5, -1.0, 0.2], [-0.3, np.nan, -0.5], [1.2, 0.1, -0.9]], dtype=np.float32
    )
    values, _ = predict_onnx(onnx_bytes, rows, "regression")

    metadata = ModelMetadata(
        run_id=uuid4(),
        problem_type="regression",
        target_column="y",
        feature_columns=["f0", "f1", "f2"],
        id_column=None,
        classes=None,
        onnx_outputs={"value": outputs.value},
        pipeline=spec,
        library_versions=library_versions(),
        helix_version="dev",
        created_at=datetime.now(UTC),
    )
    expected = {
        "ids": None,
        "rows": [
            [None if np.isnan(v) else v for v in row] for row in rows.tolist()
        ],
        "values": values.tolist(),
    }
    _write_fixture(out_dir / "regression", onnx_bytes, metadata, expected)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--out", required=True, type=Path, help="Folder to write classification/ and regression/ fixtures under."
    )
    args = parser.parse_args()

    _make_classification_fixture(args.out)
    _make_regression_fixture(args.out)


if __name__ == "__main__":
    main()
