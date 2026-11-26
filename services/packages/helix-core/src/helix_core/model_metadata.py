"""The ``metadata.json`` written next to every exported ONNX model.

Documented in ``docs/architecture.md`` ("Model artifacts") and validated by
``contracts/model-metadata.schema.json``, generated from :class:`ModelMetadata` by
:mod:`helix_core.contracts`.
"""

from datetime import datetime
from importlib.metadata import version
from typing import Literal
from uuid import UUID

from pydantic import BaseModel, ConfigDict

from helix_core.messages import PipelineSpec, ProblemType


class ModelMetadata(BaseModel):
    """Metadata describing an exported ONNX model and the pipeline it came from."""

    model_config = ConfigDict(extra="forbid")

    schema_version: Literal[1] = 1
    run_id: UUID
    problem_type: ProblemType
    target_column: str
    feature_columns: list[str]
    id_column: str | None
    classes: list[str | int | float] | None
    onnx_input_name: Literal["input"] = "input"
    onnx_outputs: dict[str, str]
    pipeline: PipelineSpec
    library_versions: dict[str, str]
    helix_version: str
    created_at: datetime


def library_versions() -> dict[str, str]:
    """Return the installed versions of the libraries used to train and export a model.

    Returns:
        A mapping of library name to version string, for ``scikit-learn``,
        ``skl2onnx``, ``onnx``, ``onnxruntime``, ``xgboost`` and ``numpy``.
    """
    return {
        "scikit-learn": version("scikit-learn"),
        "skl2onnx": version("skl2onnx"),
        "onnx": version("onnx"),
        "onnxruntime": version("onnxruntime"),
        "xgboost": version("xgboost"),
        "numpy": version("numpy"),
    }
