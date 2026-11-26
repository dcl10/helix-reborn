"""The registry of pipeline steps a :class:`~helix_core.messages.PipelineSpec` may use.

Every step converts to ONNX (design rule 3 in ``docs/architecture.md``); its test is
in ``tests/test_registry.py``. :func:`catalog` is exported to
``contracts/pipeline-catalog.json`` for the API and the web app.
"""

from collections.abc import Callable
from dataclasses import dataclass
from typing import Any, Literal

from pydantic import BaseModel
from sklearn.base import BaseEstimator
from sklearn.ensemble import RandomForestClassifier, RandomForestRegressor
from sklearn.feature_selection import (
    SelectFromModel,
    SelectKBest,
    VarianceThreshold,
    f_classif,
    f_regression,
)
from sklearn.impute import SimpleImputer
from sklearn.linear_model import (
    ElasticNet,
    Lasso,
    LinearRegression,
    LogisticRegression,
    Ridge,
)
from sklearn.naive_bayes import GaussianNB
from sklearn.neighbors import KNeighborsClassifier, KNeighborsRegressor
from sklearn.neural_network import MLPClassifier, MLPRegressor
from sklearn.preprocessing import MinMaxScaler, RobustScaler, StandardScaler
from sklearn.svm import SVC, SVR
from xgboost import XGBClassifier, XGBRegressor

from helix_core.messages import ProblemType
from helix_core.pipelines import params as p
from helix_core.pipelines.correlation import CorrelationThreshold

StepKind = Literal["preprocessor", "feature_selector", "model"]
"""What a step does: transform columns, choose columns, or predict."""

BOTH: tuple[ProblemType, ...] = ("classification", "regression")
CLASSIFICATION: tuple[ProblemType, ...] = ("classification",)
REGRESSION: tuple[ProblemType, ...] = ("regression",)

_KIND_ORDER: dict[StepKind, int] = {
    "preprocessor": 0,
    "feature_selector": 1,
    "model": 2,
}


@dataclass(frozen=True)
class BuildContext:
    """What a step's builder needs besides its parameters.

    Attributes:
        problem_type: Classification or regression.
        n_features: The number of feature columns the pipeline is fitted on.
        random_seed: Seed for every estimator that accepts ``random_state``.
        n_jobs: Cores for the estimator itself; 1 when a search parallelises instead.
    """

    problem_type: ProblemType
    n_features: int
    random_seed: int
    n_jobs: int


@dataclass(frozen=True)
class StepDefinition:
    """One step a pipeline may use.

    Attributes:
        id: The id used in a :class:`~helix_core.messages.StepSpec`.
        kind: Preprocessor, feature selector or model.
        label: A short name for the UI.
        description: One sentence for the UI.
        problem_types: The problem types the step supports.
        params_model: The step's parameters; every field has a default.
        build: Builds the unfitted estimator from validated parameters.
        search_grid: For models, the default hyperparameter grid (unprefixed
            parameter names); ``None`` if there isn't one.
    """

    id: str
    kind: StepKind
    label: str
    description: str
    problem_types: tuple[ProblemType, ...]
    params_model: type[BaseModel]
    build: Callable[[Any, BuildContext], BaseEstimator]
    search_grid: dict[str, list[Any]] | None = None


# Default search grids. The random forest, XGBoost, SVM and linear model grids are the
# original Helix's (helix/options/choices/ml_models.py).
_RANDOM_FOREST_GRID: dict[str, list[Any]] = {
    "n_estimators": [10, 50, 100, 200],
    "max_depth": [None, 10, 20, 30],
    "min_samples_split": [2, 5, 10],
}
_XGB_GRID: dict[str, list[Any]] = {
    "n_estimators": [10, 50, 100, 200],
    "max_depth": [3, 5, 7, 9],
    "learning_rate": [0.01, 0.1, 0.3],
    "subsample": [0.5, 0.8, 1.0],
}
_SVM_GRID: dict[str, list[Any]] = {
    "kernel": ["linear", "poly", "rbf"],
    "C": [0.1, 1.0, 10.0],
    "degree": [2, 3, 4],
}
_LINEAR_MODEL_GRID: dict[str, list[Any]] = {"fit_intercept": [True, False]}


def _mlp_kwargs(params: p.MlpParams, ctx: BuildContext) -> dict[str, Any]:
    return {
        "hidden_layer_sizes": tuple(params.hidden_layer_sizes),
        "activation": params.activation,
        "alpha": params.alpha,
        "learning_rate_init": params.learning_rate_init,
        "max_iter": params.max_iter,
        "random_state": ctx.random_seed,
    }


def _random_forest_kwargs(
    params: p.RandomForestParams, ctx: BuildContext
) -> dict[str, Any]:
    return {
        **params.model_dump(),
        "random_state": ctx.random_seed,
        "n_jobs": ctx.n_jobs,
    }


def _xgboost_kwargs(params: p.XGBoostParams, ctx: BuildContext) -> dict[str, Any]:
    return {
        **params.model_dump(),
        "random_state": ctx.random_seed,
        "n_jobs": ctx.n_jobs,
    }


_STEPS: list[StepDefinition] = [
    # Preprocessors
    StepDefinition(
        id="simple_imputer",
        kind="preprocessor",
        label="Fill missing values",
        description="Replace missing values with each column's mean, median or most "
        "frequent value.",
        problem_types=BOTH,
        params_model=p.SimpleImputerParams,
        build=lambda params, ctx: SimpleImputer(strategy=params.strategy),
    ),
    StepDefinition(
        id="standard_scaler",
        kind="preprocessor",
        label="Standard scaling",
        description="Scale each column to zero mean and unit variance.",
        problem_types=BOTH,
        params_model=p.StandardScalerParams,
        build=lambda params, ctx: StandardScaler(**params.model_dump()),
    ),
    StepDefinition(
        id="mean_centering",
        kind="preprocessor",
        label="Mean centering",
        description="Subtract each column's mean, without scaling.",
        problem_types=BOTH,
        params_model=p.NoParams,
        build=lambda params, ctx: StandardScaler(with_std=False),
    ),
    StepDefinition(
        id="min_max_scaler",
        kind="preprocessor",
        label="Min-max scaling",
        description="Scale each column to the range 0 to 1.",
        problem_types=BOTH,
        params_model=p.NoParams,
        build=lambda params, ctx: MinMaxScaler(),
    ),
    StepDefinition(
        id="robust_scaler",
        kind="preprocessor",
        label="Robust scaling",
        description="Scale each column by its median and interquartile range, so "
        "outliers matter less.",
        problem_types=BOTH,
        params_model=p.RobustScalerParams,
        build=lambda params, ctx: RobustScaler(**params.model_dump()),
    ),
    # Feature selectors
    StepDefinition(
        id="variance_threshold",
        kind="feature_selector",
        label="Variance threshold",
        description="Drop columns whose variance is at or below a threshold.",
        problem_types=BOTH,
        params_model=p.VarianceThresholdParams,
        build=lambda params, ctx: VarianceThreshold(threshold=params.threshold),
    ),
    StepDefinition(
        id="correlation_threshold",
        kind="feature_selector",
        label="Correlation threshold",
        description="Drop columns that are highly correlated with an earlier column.",
        problem_types=BOTH,
        params_model=p.CorrelationThresholdParams,
        build=lambda params, ctx: CorrelationThreshold(threshold=params.threshold),
    ),
    StepDefinition(
        id="select_k_best",
        kind="feature_selector",
        label="Select k best",
        description="Keep the k columns with the strongest univariate relationship to "
        "the target (ANOVA F-test or F-regression).",
        problem_types=BOTH,
        params_model=p.SelectKBestParams,
        build=lambda params, ctx: SelectKBest(
            f_classif if ctx.problem_type == "classification" else f_regression,
            k=min(params.k, ctx.n_features),
        ),
    ),
    StepDefinition(
        id="lasso_selector",
        kind="feature_selector",
        label="Lasso selection",
        description="Keep the columns a Lasso model gives non-zero weight.",
        problem_types=BOTH,
        params_model=p.LassoSelectorParams,
        build=lambda params, ctx: SelectFromModel(
            Lasso(alpha=params.alpha, random_state=ctx.random_seed)
        ),
    ),
    # Models
    StepDefinition(
        id="logistic_regression",
        kind="model",
        label="Logistic regression",
        description="A linear classifier with L2 regularisation.",
        problem_types=CLASSIFICATION,
        params_model=p.LogisticRegressionParams,
        build=lambda params, ctx: LogisticRegression(
            **params.model_dump(), random_state=ctx.random_seed
        ),
        search_grid={"C": [0.01, 0.1, 1, 10], "fit_intercept": [True, False]},
    ),
    StepDefinition(
        id="random_forest_classifier",
        kind="model",
        label="Random forest",
        description="An ensemble of decision trees, each fitted on a bootstrap sample.",
        problem_types=CLASSIFICATION,
        params_model=p.RandomForestParams,
        build=lambda params, ctx: RandomForestClassifier(
            **_random_forest_kwargs(params, ctx)
        ),
        search_grid=_RANDOM_FOREST_GRID,
    ),
    StepDefinition(
        id="random_forest_regressor",
        kind="model",
        label="Random forest",
        description="An ensemble of decision trees, each fitted on a bootstrap sample.",
        problem_types=REGRESSION,
        params_model=p.RandomForestParams,
        build=lambda params, ctx: RandomForestRegressor(
            **_random_forest_kwargs(params, ctx)
        ),
        search_grid=_RANDOM_FOREST_GRID,
    ),
    StepDefinition(
        id="xgboost_classifier",
        kind="model",
        label="XGBoost",
        description="Gradient-boosted decision trees.",
        problem_types=CLASSIFICATION,
        params_model=p.XGBoostParams,
        build=lambda params, ctx: XGBClassifier(**_xgboost_kwargs(params, ctx)),
        search_grid=_XGB_GRID,
    ),
    StepDefinition(
        id="xgboost_regressor",
        kind="model",
        label="XGBoost",
        description="Gradient-boosted decision trees.",
        problem_types=REGRESSION,
        params_model=p.XGBoostParams,
        build=lambda params, ctx: XGBRegressor(**_xgboost_kwargs(params, ctx)),
        search_grid=_XGB_GRID,
    ),
    StepDefinition(
        id="svc",
        kind="model",
        label="Support vector machine",
        description="A kernel support vector classifier, with probability estimates.",
        problem_types=CLASSIFICATION,
        params_model=p.SvmParams,
        build=lambda params, ctx: SVC(
            **params.model_dump(), probability=True, random_state=ctx.random_seed
        ),
        search_grid=_SVM_GRID,
    ),
    StepDefinition(
        id="svr",
        kind="model",
        label="Support vector machine",
        description="A kernel support vector regressor.",
        problem_types=REGRESSION,
        params_model=p.SvmParams,
        build=lambda params, ctx: SVR(**params.model_dump()),
        search_grid=_SVM_GRID,
    ),
    StepDefinition(
        id="knn_classifier",
        kind="model",
        label="k-nearest neighbours",
        description="Predict the majority class of the nearest training rows.",
        problem_types=CLASSIFICATION,
        params_model=p.KnnParams,
        build=lambda params, ctx: KNeighborsClassifier(
            **params.model_dump(), n_jobs=ctx.n_jobs
        ),
        search_grid={"n_neighbors": [3, 5, 7, 11], "weights": ["uniform", "distance"]},
    ),
    StepDefinition(
        id="knn_regressor",
        kind="model",
        label="k-nearest neighbours",
        description="Predict the average target of the nearest training rows.",
        problem_types=REGRESSION,
        params_model=p.KnnParams,
        build=lambda params, ctx: KNeighborsRegressor(
            **params.model_dump(), n_jobs=ctx.n_jobs
        ),
        search_grid={"n_neighbors": [3, 5, 7, 11], "weights": ["uniform", "distance"]},
    ),
    StepDefinition(
        id="mlp_classifier",
        kind="model",
        label="Neural network",
        description="A multi-layer perceptron classifier.",
        problem_types=CLASSIFICATION,
        params_model=p.MlpParams,
        build=lambda params, ctx: MLPClassifier(**_mlp_kwargs(params, ctx)),
        search_grid={
            "alpha": [0.0001, 0.001, 0.01],
            "hidden_layer_sizes": [[50], [100], [100, 50]],
        },
    ),
    StepDefinition(
        id="mlp_regressor",
        kind="model",
        label="Neural network",
        description="A multi-layer perceptron regressor.",
        problem_types=REGRESSION,
        params_model=p.MlpParams,
        build=lambda params, ctx: MLPRegressor(**_mlp_kwargs(params, ctx)),
        search_grid={
            "alpha": [0.0001, 0.001, 0.01],
            "hidden_layer_sizes": [[50], [100], [100, 50]],
        },
    ),
    StepDefinition(
        id="gaussian_nb",
        kind="model",
        label="Gaussian naive Bayes",
        description="A naive Bayes classifier assuming normally distributed features.",
        problem_types=CLASSIFICATION,
        params_model=p.GaussianNbParams,
        build=lambda params, ctx: GaussianNB(var_smoothing=params.var_smoothing),
        search_grid={"var_smoothing": [1e-9, 1e-8, 1e-7]},
    ),
    StepDefinition(
        id="linear_regression",
        kind="model",
        label="Linear regression",
        description="Ordinary least squares, without regularisation.",
        problem_types=REGRESSION,
        params_model=p.LinearRegressionParams,
        build=lambda params, ctx: LinearRegression(**params.model_dump()),
        search_grid=_LINEAR_MODEL_GRID,
    ),
    StepDefinition(
        id="ridge",
        kind="model",
        label="Ridge regression",
        description="Linear regression with L2 regularisation.",
        problem_types=REGRESSION,
        params_model=p.RidgeParams,
        build=lambda params, ctx: Ridge(
            **params.model_dump(), random_state=ctx.random_seed
        ),
        search_grid={"alpha": [0.01, 0.1, 1, 10]},
    ),
    StepDefinition(
        id="lasso",
        kind="model",
        label="Lasso regression",
        description="Linear regression with L1 regularisation, which zeroes weak "
        "features.",
        problem_types=REGRESSION,
        params_model=p.LassoParams,
        build=lambda params, ctx: Lasso(
            **params.model_dump(), random_state=ctx.random_seed
        ),
        search_grid={"alpha": [0.001, 0.01, 0.1, 1]},
    ),
    StepDefinition(
        id="elastic_net",
        kind="model",
        label="Elastic net",
        description="Linear regression with a mix of L1 and L2 regularisation.",
        problem_types=REGRESSION,
        params_model=p.ElasticNetParams,
        build=lambda params, ctx: ElasticNet(
            **params.model_dump(), random_state=ctx.random_seed
        ),
        search_grid={"alpha": [0.01, 0.1, 1], "l1_ratio": [0.2, 0.5, 0.8]},
    ),
]

REGISTRY: dict[str, StepDefinition] = {step.id: step for step in _STEPS}
"""Every step a pipeline may use, by id."""


def catalog() -> dict[str, Any]:
    """Build the pipeline catalog: every step, its parameters' JSON Schema and grid.

    Returns:
        ``{"schema_version": 1, "steps": [...]}``, each step being ``{id, kind,
        label, description, problem_types, params_schema, search_grid}``, sorted by
        kind (preprocessors, feature selectors, models) then id.
    """
    steps = sorted(REGISTRY.values(), key=lambda s: (_KIND_ORDER[s.kind], s.id))
    return {
        "schema_version": 1,
        "steps": [
            {
                "id": step.id,
                "kind": step.kind,
                "label": step.label,
                "description": step.description,
                "problem_types": list(step.problem_types),
                "params_schema": step.params_model.model_json_schema(),
                "search_grid": step.search_grid,
            }
            for step in steps
        ],
    }
