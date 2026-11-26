"""Advice about pipeline specs that are valid but probably not what the user wants.

Decision trees split one column at a time on the order of its values, so trees and
tree ensembles are invariant to per-column monotone transforms: every scaler in the
catalog has no effect on them (*The Elements of Statistical Learning*; Breiman et al.,
*CART*, 1984). A scaler in front of one can still make the ONNX export drift past
:func:`~helix_core.onnx_export.check_parity`'s budget (see ``docs/pipelines.md``). The
training worker adds this to a parity failure's message, and the web app's pipeline
builder (#26) shows the same advice as a warning, with the same rule.
"""

from helix_core.messages import PipelineSpec

TREE_MODELS = frozenset(
    {
        "random_forest_classifier",
        "random_forest_regressor",
        "xgboost_classifier",
        "xgboost_regressor",
    }
)
"""Models that split on one column's value order at a time."""

SCALERS = frozenset(
    {"standard_scaler", "mean_centering", "min_max_scaler", "robust_scaler"}
)
"""Steps that rescale each column affinely."""


def _scale_sensitive_after(scaler: str, later_step: str) -> bool:
    """Whether ``later_step`` gives a different result when ``scaler`` runs first.

    ``lasso_selector``'s penalty depends on scale. ``variance_threshold`` uses an
    absolute variance cutoff, so it depends on scaling but not on centering alone.
    Imputing, other scalers, ``correlation_threshold`` (Pearson correlation) and
    ``select_k_best`` (F-statistics) are unaffected by per-column affine scaling.
    """
    if later_step == "lasso_selector":
        return True
    return later_step == "variance_threshold" and scaler != "mean_centering"


def redundant_scaler_steps(spec: PipelineSpec) -> list[str]:
    """Find scaler steps that have no effect because a tree model follows them.

    Args:
        spec: The pipeline spec.

    Returns:
        The ids of scaler steps, in pipeline order and without duplicates, that come
        before a tree model with no scale-sensitive step after them. Empty when the
        model isn't a tree model.
    """
    if spec.model.step not in TREE_MODELS:
        return []

    step_ids = [step.step for step in spec.steps]
    redundant: list[str] = []
    for i, step_id in enumerate(step_ids):
        if step_id not in SCALERS or step_id in redundant:
            continue
        if not any(
            _scale_sensitive_after(step_id, later) for later in step_ids[i + 1 :]
        ):
            redundant.append(step_id)
    return redundant
