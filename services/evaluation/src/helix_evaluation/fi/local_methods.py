"""Local feature importance: LIME and per-sample SHAP values.

Ported from the original Helix's
``helix/services/feature_importance/local_methods.py`` (``calculate_lime_values``,
``calculate_local_shap_values``), dropping the ``Logger`` argument. Unlike the
original, LIME weights are mapped back to their feature by index (the original read
``local_exp[1]``, which is ordered by importance and assumes label 1), so every
column lines up with its feature.
"""

import logging
import warnings

import numpy as np
import pandas as pd
import shap
from lime.lime_tabular import LimeTabularExplainer

from helix_core.messages import ProblemType
from helix_evaluation.model import OnnxClassifier, OnnxRegressor

logger = logging.getLogger(__name__)


def _lime_label(explanation: object, n_classes: int) -> int:
    """Pick the label whose LIME weights describe an explanation.

    Args:
        explanation: A LIME ``Explanation`` from ``explain_instance``.
        n_classes: The number of classes, or 0 for regression.

    Returns:
        ``1`` (the positive class) for binary classification, the single top label
        for multi-class, and the only key of ``as_map()`` for regression.
    """
    if n_classes == 2:
        return 1
    if n_classes > 2:
        return int(explanation.top_labels[0])
    return next(iter(explanation.as_map()))


def lime_values(
    model: OnnxClassifier | OnnxRegressor,
    x_train: pd.DataFrame,
    x_explain: pd.DataFrame,
    problem_type: ProblemType,
    random_state: int,
) -> pd.DataFrame:
    """Compute LIME weights for each row of ``x_explain``.

    Classification explains ``model.predict_proba``: label 1 for binary problems,
    the top predicted label for multi-class. Regression explains ``model.predict``.

    Args:
        model: The fitted model to explain.
        x_train: The training rows LIME samples its perturbations from.
        x_explain: The rows to explain.
        problem_type: ``"classification"`` or ``"regression"``.
        random_state: The seed for LIME's perturbation sampling.

    Returns:
        A dataframe with one row per explained sample (``x_explain``'s index), one
        column per feature (``x_train``'s column order) and LIME weights as values.
    """
    columns = list(x_train.columns)
    n_classes = (
        model.predict_proba(x_explain.iloc[:1]).shape[1]
        if problem_type == "classification"
        else 0
    )

    logger.info("Calculating LIME values for %d samples", len(x_explain))
    rows = []
    with warnings.catch_warnings():
        warnings.simplefilter("ignore")
        explainer = LimeTabularExplainer(
            x_train.to_numpy(),
            feature_names=columns,
            mode=problem_type,
            random_state=random_state,
            discretize_continuous=True,
        )
        for _, row in x_explain.iterrows():
            if n_classes == 2:
                explanation = explainer.explain_instance(
                    row.to_numpy(),
                    model.predict_proba,
                    num_features=len(columns),
                    labels=(1,),
                )
            elif n_classes > 2:
                explanation = explainer.explain_instance(
                    row.to_numpy(),
                    model.predict_proba,
                    num_features=len(columns),
                    top_labels=1,
                )
            else:
                explanation = explainer.explain_instance(
                    row.to_numpy(), model.predict, num_features=len(columns)
                )

            weights = np.zeros(len(columns))
            label = _lime_label(explanation, n_classes)
            for feature_index, weight in explanation.as_map()[label]:
                weights[feature_index] = weight
            rows.append(weights)

    return pd.DataFrame(rows, columns=columns, index=x_explain.index)


def local_shap_values(
    model: OnnxClassifier | OnnxRegressor,
    x_background: pd.DataFrame,
    x_explain: pd.DataFrame,
) -> tuple[pd.DataFrame, shap.Explanation]:
    """Compute per-sample SHAP values for each row of ``x_explain``.

    Args:
        model: The fitted model to explain (its ``predict`` output is explained).
        x_background: The background dataset the explainer is built against.
        x_explain: The rows to explain.

    Returns:
        A tuple of a dataframe with one row per explained sample (``x_explain``'s
        index) and one column per feature, holding SHAP values, and the raw
        :class:`~shap.Explanation`.
    """
    explainer = shap.Explainer(model.predict, x_background)
    explanation = explainer(x_explain)
    df = pd.DataFrame(
        explanation.values, columns=x_explain.columns, index=x_explain.index
    )
    return df, explanation
