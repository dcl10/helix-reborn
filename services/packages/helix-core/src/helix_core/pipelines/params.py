"""Parameter models for every pipeline step in the registry.

Each model is the ``params`` of one step in a
:class:`~helix_core.messages.PipelineSpec`. Every field has a default, unknown fields
are rejected, and the constraints become the JSON Schema in
``contracts/pipeline-catalog.json`` that the API validates against and the web app
builds its forms from.
"""

from typing import Annotated, Literal

from pydantic import BaseModel, ConfigDict, Field


class StepParams(BaseModel):
    """Base class for step parameters: unknown fields are rejected."""

    model_config = ConfigDict(extra="forbid")


class NoParams(StepParams):
    """A step with no parameters."""


class SimpleImputerParams(StepParams):
    """Parameters for ``simple_imputer``."""

    strategy: Literal["mean", "median", "most_frequent"] = Field(
        "median", description="The value each missing value is replaced with."
    )


class StandardScalerParams(StepParams):
    """Parameters for ``standard_scaler``."""

    with_mean: bool = Field(True, description="Subtract each column's mean.")
    with_std: bool = Field(
        True, description="Divide by each column's standard deviation."
    )


class RobustScalerParams(StepParams):
    """Parameters for ``robust_scaler``."""

    with_centering: bool = Field(True, description="Subtract each column's median.")
    with_scaling: bool = Field(
        True, description="Divide by each column's interquartile range."
    )


class VarianceThresholdParams(StepParams):
    """Parameters for ``variance_threshold``."""

    threshold: float = Field(
        0.01, ge=0, description="Drop columns whose variance is at or below this."
    )


class CorrelationThresholdParams(StepParams):
    """Parameters for ``correlation_threshold``."""

    threshold: float = Field(
        0.9,
        ge=0.5,
        le=1.0,
        description=(
            "Drop a column when its absolute correlation with an earlier column is "
            "above this."
        ),
    )


class SelectKBestParams(StepParams):
    """Parameters for ``select_k_best``."""

    k: int = Field(
        10,
        ge=1,
        description="How many columns to keep. More than there are keeps them all.",
    )


class LassoSelectorParams(StepParams):
    """Parameters for ``lasso_selector``."""

    alpha: float = Field(
        0.05, gt=0, description="Lasso penalty strength; higher keeps fewer columns."
    )


class LogisticRegressionParams(StepParams):
    """Parameters for ``logistic_regression``."""

    C: float = Field(1.0, gt=0, description="Inverse regularisation strength.")
    fit_intercept: bool = Field(True, description="Fit an intercept term.")
    max_iter: int = Field(1000, ge=100, le=10000, description="Maximum iterations.")


class RandomForestParams(StepParams):
    """Parameters for ``random_forest_classifier`` and ``random_forest_regressor``."""

    n_estimators: int = Field(100, ge=10, le=1000, description="Number of trees.")
    max_depth: int | None = Field(
        None, ge=1, le=100, description="Maximum tree depth; empty for no limit."
    )
    min_samples_split: int = Field(
        2, ge=2, le=50, description="Minimum samples to split a node."
    )
    min_samples_leaf: int = Field(
        1, ge=1, le=50, description="Minimum samples in a leaf."
    )


class XGBoostParams(StepParams):
    """Parameters for ``xgboost_classifier`` and ``xgboost_regressor``."""

    n_estimators: int = Field(
        100, ge=10, le=2000, description="Number of boosting rounds."
    )
    max_depth: int = Field(6, ge=1, le=20, description="Maximum tree depth.")
    learning_rate: float = Field(
        0.3, ge=0.001, le=1, description="Step size shrinkage."
    )
    subsample: float = Field(
        1.0, ge=0.1, le=1, description="Share of rows sampled for each tree."
    )


class SvmParams(StepParams):
    """Parameters for ``svc`` and ``svr``."""

    C: float = Field(1.0, gt=0, description="Inverse regularisation strength.")
    kernel: Literal["linear", "poly", "rbf", "sigmoid"] = Field(
        "rbf", description="Kernel function."
    )
    degree: int = Field(3, ge=2, le=5, description="Degree of the poly kernel.")
    gamma: Literal["scale", "auto"] = Field(
        "scale", description="Kernel coefficient for rbf, poly and sigmoid."
    )


class KnnParams(StepParams):
    """Parameters for ``knn_classifier`` and ``knn_regressor``."""

    n_neighbors: int = Field(5, ge=1, le=100, description="Number of neighbours.")
    weights: Literal["uniform", "distance"] = Field(
        "uniform", description="How neighbours are weighted."
    )


class MlpParams(StepParams):
    """Parameters for ``mlp_classifier`` and ``mlp_regressor``."""

    hidden_layer_sizes: list[Annotated[int, Field(ge=1, le=1000)]] = Field(
        [100],
        min_length=1,
        max_length=5,
        description="Neurons in each hidden layer, 1 to 5 layers.",
    )
    activation: Literal["identity", "logistic", "tanh", "relu"] = Field(
        "relu", description="Hidden layer activation function."
    )
    alpha: float = Field(0.0001, ge=0, description="L2 regularisation strength.")
    learning_rate_init: float = Field(0.001, gt=0, description="Initial learning rate.")
    max_iter: int = Field(500, ge=50, le=5000, description="Maximum epochs.")


class GaussianNbParams(StepParams):
    """Parameters for ``gaussian_nb``."""

    var_smoothing: float = Field(
        1e-9, gt=0, description="Share of the largest variance added for stability."
    )


class LinearRegressionParams(StepParams):
    """Parameters for ``linear_regression``."""

    fit_intercept: bool = Field(True, description="Fit an intercept term.")


class RidgeParams(StepParams):
    """Parameters for ``ridge``."""

    alpha: float = Field(1.0, ge=0, description="L2 regularisation strength.")
    fit_intercept: bool = Field(True, description="Fit an intercept term.")


class LassoParams(StepParams):
    """Parameters for ``lasso``."""

    alpha: float = Field(1.0, gt=0, description="L1 regularisation strength.")
    fit_intercept: bool = Field(True, description="Fit an intercept term.")
    max_iter: int = Field(1000, ge=1, description="Maximum iterations.")


class ElasticNetParams(StepParams):
    """Parameters for ``elastic_net``."""

    alpha: float = Field(1.0, gt=0, description="Regularisation strength.")
    l1_ratio: float = Field(
        0.5, ge=0, le=1, description="Mix of L1 (1) and L2 (0) regularisation."
    )
