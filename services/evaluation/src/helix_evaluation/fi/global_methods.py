"""Global feature importance: permutation importance and SHAP.

Ported from the original Helix's
``helix/services/feature_importance/global_methods.py``, dropping the ``Logger``
argument (the worker logs through ``logging.getLogger`` elsewhere) and returning
dataframes shaped for :mod:`helix_evaluation.steps`.
"""

import numpy as np
import pandas as pd
import shap
from sklearn.inspection import permutation_importance


def permutation_importance_df(
    model: object,
    x: pd.DataFrame,
    y: np.ndarray,
    scoring: str,
    n_repeats: int,
    random_state: int,
    n_jobs: int,
) -> pd.DataFrame:
    """Compute permutation importance for a fitted model.

    Args:
        model: A fitted scikit-learn-compatible estimator.
        x: The feature rows to permute.
        y: The true target values aligned to ``x``.
        scoring: The scikit-learn scoring string to evaluate each permutation with.
        n_repeats: The number of times each feature is permuted.
        random_state: The seed for permutation shuffling.
        n_jobs: The number of parallel jobs.

    Returns:
        A dataframe indexed by feature name with columns ``importance_mean`` and
        ``importance_std``, sorted by ``importance_mean`` descending.
    """
    result = permutation_importance(
        model,
        x,
        y,
        scoring=scoring,
        n_repeats=n_repeats,
        random_state=random_state,
        n_jobs=n_jobs,
    )
    df = pd.DataFrame(
        {
            "importance_mean": result.importances_mean,
            "importance_std": result.importances_std,
        },
        index=x.columns,
    )
    return df.sort_values(by="importance_mean", ascending=False)


def shap_global_df(
    model: object, x_background: pd.DataFrame, x_explain: pd.DataFrame
) -> tuple[pd.DataFrame, shap.Explanation]:
    """Compute global SHAP importance for a fitted model.

    Args:
        model: A fitted scikit-learn-compatible estimator.
        x_background: The background dataset the explainer is built against.
        x_explain: The rows to explain.

    Returns:
        A tuple of a dataframe indexed by feature name with column
        ``mean_abs_shap``, sorted descending, and the raw SHAP
        :class:`~shap.Explanation`.
    """
    explainer = shap.Explainer(model.predict, x_background)
    explanation = explainer(x_explain)

    mean_abs = np.abs(explanation.values).mean(axis=0)
    df = pd.DataFrame({"mean_abs_shap": mean_abs}, index=x_explain.columns)
    return df.sort_values(by="mean_abs_shap", ascending=False), explanation


def normalise(series: pd.Series) -> pd.Series:
    """Min-max scale a series to ``[0, 1]``.

    Args:
        series: The values to scale.

    Returns:
        The scaled series. A constant series (including a single-element one)
        becomes all zeros.
    """
    minimum = series.min()
    maximum = series.max()
    if maximum == minimum:
        return pd.Series(0.0, index=series.index)
    return (series - minimum) / (maximum - minimum)
