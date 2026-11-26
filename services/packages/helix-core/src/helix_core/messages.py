"""Wire message models for jobs and job events (RabbitMQ).

These pydantic models are the source of truth for the shapes documented in
``docs/architecture.md`` ("Messaging"). They are exported to JSON Schemas under
``contracts/messages/`` by :mod:`helix_core.contracts`, which the C# API tests
validate against.
"""

from datetime import UTC, datetime
from typing import Any, Literal
from uuid import UUID

from pydantic import BaseModel, ConfigDict, Field, model_validator

SCHEMA_VERSION = 1
"""Current wire schema version, stamped onto every message as ``schema_version``."""

ProblemType = Literal["classification", "regression"]

PlotStyle = Literal["white", "dark", "whitegrid", "darkgrid", "ticks"]


class StepSpec(BaseModel):
    """One step in a pipeline: a catalog id plus its parameters.

    The ``step`` id is validated against the pipeline catalog registry (see #22);
    this model only constrains its shape.
    """

    model_config = ConfigDict(extra="forbid")

    step: str
    params: dict[str, Any] = {}


class SplitSpec(BaseModel):
    """How a dataset is split into training and test portions."""

    model_config = ConfigDict(extra="forbid")

    test_size: float = Field(0.2, ge=0.05, le=0.5)
    random_seed: int = 42
    cv_folds: int | None = Field(None, ge=2, le=10)


class SearchSpec(BaseModel):
    """Hyperparameter search configuration on the training portion."""

    model_config = ConfigDict(extra="forbid")

    method: Literal["grid", "random"]
    cv_folds: int = Field(5, ge=2, le=10)
    n_iter: int | None = Field(None, ge=1, le=500)
    param_grid: dict[str, list[Any]] | None = None

    @model_validator(mode="after")
    def _check_n_iter_required_for_random(self) -> "SearchSpec":
        """Require ``n_iter`` when ``method`` is ``"random"``.

        Returns:
            This instance, unchanged.

        Raises:
            ValueError: If ``method == "random"`` and ``n_iter`` is ``None``.
        """
        if self.method == "random" and self.n_iter is None:
            raise ValueError("n_iter is required when method is 'random'")
        return self


class PipelineSpec(BaseModel):
    """The full specification of a training pipeline.

    Sent as part of :class:`TrainingJob`. ``steps`` are applied in order before the
    single ``model`` step.
    """

    model_config = ConfigDict(extra="forbid")

    problem_type: ProblemType
    target_column: str
    feature_columns: list[str] = Field(min_length=1)
    id_column: str | None = None
    split: SplitSpec = SplitSpec()
    steps: list[StepSpec] = []
    model: StepSpec
    search: SearchSpec | None = None

    @model_validator(mode="after")
    def _check_columns(self) -> "PipelineSpec":
        """Cross-check target, id and feature column names.

        Returns:
            This instance, unchanged.

        Raises:
            ValueError: If the target or id column is also a feature column, or if
                ``feature_columns`` contains duplicates.
        """
        if len(set(self.feature_columns)) != len(self.feature_columns):
            raise ValueError("feature_columns must not contain duplicates")
        if self.target_column in self.feature_columns:
            raise ValueError("target_column must not be one of feature_columns")
        if self.id_column is not None and self.id_column in self.feature_columns:
            raise ValueError("id_column must not be one of feature_columns")
        return self


class TrainingJob(BaseModel):
    """A training job sent from the API to the training worker on ``training.jobs``."""

    model_config = ConfigDict(extra="forbid")

    schema_version: Literal[1] = 1
    job_id: UUID
    run_id: UUID
    experiment_id: UUID
    dataset_key: str
    output_prefix: str
    pipeline: PipelineSpec


class FuzzyOptions(BaseModel):
    """Options for fuzzy-logic interpretability outputs during evaluation."""

    model_config = ConfigDict(extra="forbid")

    number_fuzzy_features: int = Field(10, ge=1)
    granular_features: bool = True
    number_clusters: int = Field(5, ge=2, le=10)
    cluster_names: list[str] = ["very low", "low", "medium", "high", "very high"]
    number_rules: int = Field(5, ge=1)

    @model_validator(mode="after")
    def _check_cluster_names_length(self) -> "FuzzyOptions":
        """Require ``cluster_names`` to have one name per cluster.

        Returns:
            This instance, unchanged.

        Raises:
            ValueError: If ``len(cluster_names) != number_clusters``.
        """
        if len(self.cluster_names) != self.number_clusters:
            raise ValueError("cluster_names must have exactly number_clusters entries")
        return self


class PlotOptions(BaseModel):
    """Shared plot styling options.

    Part of the wire contract because :class:`EvaluationOptions` sends it to the
    evaluation worker; also reused by the EDA service (#17).
    """

    model_config = ConfigDict(extra="forbid")

    dpi: int = Field(150, ge=50, le=600)
    width: float = 10
    height: float = 8
    title: str | None = None
    style: PlotStyle = "whitegrid"
    colour_map: str = "viridis"
    axis_font_size: int = 12
    tick_size: int = 10
    title_font_size: int = 14
    font_family: str = "sans-serif"
    rotate_x_labels: int = 0
    rotate_y_labels: int = 0


class EvaluationOptions(BaseModel):
    """Options controlling what the evaluation worker computes and plots."""

    model_config = ConfigDict(extra="forbid")

    plots: bool = True
    global_methods: list[Literal["permutation", "shap"]] = ["permutation", "shap"]
    permutation_repeats: int = Field(10, ge=1, le=100)
    local_methods: list[Literal["lime", "shap"]] = []
    local_max_samples: int = Field(50, ge=1, le=1000)
    ensemble_methods: list[Literal["mean", "majority_vote"]] = []
    num_features_to_plot: int = Field(10, ge=1, le=50)
    fuzzy: FuzzyOptions | None = None
    plot_options: PlotOptions | None = None

    @model_validator(mode="after")
    def _check_fuzzy_requirements(self) -> "EvaluationOptions":
        """Require at least one local and one ensemble method when ``fuzzy`` is set.

        Returns:
            This instance, unchanged.

        Raises:
            ValueError: If ``fuzzy`` is set but ``local_methods`` or
                ``ensemble_methods`` is empty.
        """
        if self.fuzzy is not None and (
            not self.local_methods or not self.ensemble_methods
        ):
            raise ValueError(
                "fuzzy requires at least one local method and one ensemble method"
            )
        return self


class EvaluationJob(BaseModel):
    """An evaluation job sent from the API to the evaluation worker.

    Sent on ``evaluation.jobs``.
    """

    model_config = ConfigDict(extra="forbid")

    schema_version: Literal[1] = 1
    job_id: UUID
    evaluation_id: UUID
    run_id: UUID
    experiment_id: UUID
    dataset_key: str
    model_prefix: str
    output_prefix: str
    options: EvaluationOptions = EvaluationOptions()


class JobError(BaseModel):
    """The error reported on a ``failed`` :class:`JobEvent`."""

    model_config = ConfigDict(extra="forbid")

    type: str
    message: str


class JobEvent(BaseModel):
    """A progress or outcome event published by a worker to ``jobs.events``."""

    model_config = ConfigDict(extra="forbid")

    schema_version: Literal[1] = 1
    job_id: UUID
    job_type: Literal["training", "evaluation"]
    entity_id: UUID
    status: Literal["started", "progress", "succeeded", "failed"]
    progress: float | None = Field(None, ge=0, le=1)
    message: str | None = None
    result: dict[str, Any] | None = None
    error: JobError | None = None
    timestamp: datetime = Field(default_factory=lambda: datetime.now(UTC))
