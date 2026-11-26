"""Turn a :class:`~helix_core.messages.PipelineSpec` into a scikit-learn ``Pipeline``.

:func:`validate_spec` checks a spec against the step registry (the API does the same
against ``contracts/pipeline-catalog.json``);
:func:`build_pipeline` builds the unfitted pipeline and :func:`search_space` the
hyperparameter grid to search.
"""

from typing import Any

from pydantic import BaseModel, ValidationError
from sklearn.pipeline import Pipeline

from helix_core.errors import InvalidSpecError
from helix_core.messages import PipelineSpec, StepSpec
from helix_core.pipelines.registry import REGISTRY, BuildContext, StepDefinition


def _describe(error: ValidationError) -> str:
    return "; ".join(
        f"{'.'.join(str(part) for part in e['loc']) or 'params'}: {e['msg']}"
        for e in error.errors()
    )


def _definition(step: StepSpec) -> StepDefinition:
    definition = REGISTRY.get(step.step)
    if definition is None:
        raise InvalidSpecError(f"Unknown step '{step.step}'.")
    return definition


def _validated_params(step: StepSpec, definition: StepDefinition) -> BaseModel:
    try:
        return definition.params_model.model_validate(step.params)
    except ValidationError as error:
        raise InvalidSpecError(
            f"Invalid parameters for '{step.step}': {_describe(error)}"
        ) from error


def _check_problem_type(
    step: StepSpec, definition: StepDefinition, spec: PipelineSpec
) -> None:
    if spec.problem_type not in definition.problem_types:
        raise InvalidSpecError(
            f"'{step.step}' doesn't support {spec.problem_type}; it supports "
            f"{' and '.join(definition.problem_types)}."
        )


def validate_spec(spec: PipelineSpec) -> None:
    """Check every step, its parameters and the search against the registry.

    Args:
        spec: The pipeline spec to check.

    Raises:
        InvalidSpecError: If a step id is unknown; ``steps`` contains a model or
            ``model`` isn't one; a step doesn't support the spec's problem type; a
            step's parameters are invalid; ``search.param_grid`` has an unknown key,
            an empty list or an invalid value; or there's a search but neither a
            ``param_grid`` nor a default grid for the model.
    """
    for step in spec.steps:
        definition = _definition(step)
        if definition.kind == "model":
            raise InvalidSpecError(
                f"'{step.step}' is a model; put it in 'model', not 'steps'."
            )
        _check_problem_type(step, definition, spec)
        _validated_params(step, definition)

    model = _definition(spec.model)
    if model.kind != "model":
        raise InvalidSpecError(f"'{spec.model.step}' isn't a model.")
    _check_problem_type(spec.model, model, spec)
    _validated_params(spec.model, model)

    if spec.search is None:
        return
    if spec.search.param_grid is None:
        if model.search_grid is None:
            raise InvalidSpecError(
                f"'{spec.model.step}' has no default search grid; give a param_grid."
            )
        return

    fields = model.params_model.model_fields
    for key, values in spec.search.param_grid.items():
        if key not in fields:
            raise InvalidSpecError(
                f"Unknown parameter '{key}' in param_grid for '{spec.model.step}'."
            )
        if not values:
            raise InvalidSpecError(f"param_grid['{key}'] needs at least one value.")
        for value in values:
            try:
                model.params_model.model_validate({key: value})
            except ValidationError as error:
                raise InvalidSpecError(
                    f"Invalid value {value!r} in param_grid['{key}']: "
                    f"{_describe(error)}"
                ) from error


def build_pipeline(spec: PipelineSpec, n_features: int, n_jobs: int) -> Pipeline:
    """Validate a spec and build its unfitted pipeline.

    Steps are named ``s{i}_{step id}`` in order, then ``model``. With a search, the
    model gets ``n_jobs=1`` since the search parallelises instead.

    Args:
        spec: The pipeline spec.
        n_features: The number of feature columns the pipeline will be fitted on.
        n_jobs: Cores available for training.

    Returns:
        The unfitted pipeline.

    Raises:
        InvalidSpecError: If the spec is invalid (see :func:`validate_spec`).
    """
    validate_spec(spec)
    ctx = BuildContext(
        problem_type=spec.problem_type,
        n_features=n_features,
        random_seed=spec.split.random_seed,
        n_jobs=1 if spec.search is not None else n_jobs,
    )

    named_steps = []
    for i, step in enumerate(spec.steps):
        definition = REGISTRY[step.step]
        estimator = definition.build(_validated_params(step, definition), ctx)
        named_steps.append((f"s{i}_{step.step}", estimator))

    model = REGISTRY[spec.model.step]
    named_steps.append(
        ("model", model.build(_validated_params(spec.model, model), ctx))
    )
    return Pipeline(named_steps)


def _estimator_value(key: str, value: Any) -> Any:
    """Convert a JSON grid value to what the estimator expects."""
    if key == "hidden_layer_sizes" and isinstance(value, list):
        return tuple(value)
    return value


def search_space(spec: PipelineSpec) -> dict[str, list[Any]]:
    """Return the hyperparameter grid to search, keyed for the built pipeline.

    Args:
        spec: A pipeline spec with a ``search``.

    Returns:
        ``search.param_grid``, or the model's default grid, with each key prefixed
        ``model__`` and MLP layer sizes as tuples.

    Raises:
        InvalidSpecError: If the spec is invalid or has no ``search``.
    """
    validate_spec(spec)
    if spec.search is None:
        raise InvalidSpecError("This pipeline has no hyperparameter search.")

    grid = spec.search.param_grid or REGISTRY[spec.model.step].search_grid or {}
    return {
        f"model__{key}": [_estimator_value(key, value) for value in values]
        for key, values in grid.items()
    }
