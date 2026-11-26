"""Pipeline steps, the spec-to-``Pipeline`` builder and the pipeline catalog.

See ``docs/pipelines.md``.
"""

from helix_core.pipelines.advice import redundant_scaler_steps
from helix_core.pipelines.builder import build_pipeline, search_space, validate_spec
from helix_core.pipelines.correlation import CorrelationThreshold
from helix_core.pipelines.registry import (
    REGISTRY,
    BuildContext,
    StepDefinition,
    StepKind,
    catalog,
)

__all__ = [
    "REGISTRY",
    "BuildContext",
    "CorrelationThreshold",
    "StepDefinition",
    "StepKind",
    "build_pipeline",
    "catalog",
    "redundant_scaler_steps",
    "search_space",
    "validate_spec",
]
