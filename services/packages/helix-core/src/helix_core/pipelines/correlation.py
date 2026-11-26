"""``CorrelationThreshold``: drop columns highly correlated with an earlier column.

Ported from the original Helix's ``run_feature_selection``. It's a plain
scikit-learn selector, so it converts to ONNX with the same converter skl2onnx uses
for ``VarianceThreshold`` (registered by :func:`register_correlation_converter`).
"""

import numpy as np
import pandas as pd
from sklearn.base import BaseEstimator
from sklearn.feature_selection import SelectorMixin
from sklearn.utils.validation import check_is_fitted, validate_data


class CorrelationThreshold(SelectorMixin, BaseEstimator):
    """Drop columns highly correlated with an earlier column.

    A column is dropped when its absolute Pearson correlation with any earlier
    column is above ``threshold``. Correlations are computed pairwise, ignoring
    missing values. A constant column has no defined correlation, so it's kept.

    Args:
        threshold: The absolute correlation above which a later column is dropped.
            ``1.0`` keeps every column.
    """

    def __init__(self, threshold: float = 0.9) -> None:
        self.threshold = threshold

    def fit(self, x: np.ndarray, y: np.ndarray | None = None) -> "CorrelationThreshold":
        """Find the columns to keep.

        Args:
            x: The training features.
            y: Ignored.

        Returns:
            This selector, fitted.
        """
        x = validate_data(self, x, ensure_all_finite="allow-nan", dtype=np.float64)
        correlation = pd.DataFrame(x).corr().abs().to_numpy()
        upper = np.triu(correlation, k=1)
        # NaN (a constant column) compares False, so it never causes a drop.
        self.support_ = ~np.any(upper > self.threshold, axis=0)
        return self

    def _get_support_mask(self) -> np.ndarray:
        """Return the boolean mask of kept columns.

        Returns:
            ``True`` for each column kept.
        """
        check_is_fitted(self, "support_")
        return self.support_

    def __sklearn_tags__(self):  # noqa: ANN204 - sklearn's Tags type is private
        """Declare that missing values are allowed.

        Returns:
            The estimator's scikit-learn tags.
        """
        tags = super().__sklearn_tags__()
        tags.input_tags.allow_nan = True
        return tags


def register_correlation_converter() -> None:
    """Register :class:`CorrelationThreshold` with skl2onnx.

    Reuses the shape calculator and converter skl2onnx uses for ``VarianceThreshold``:
    both only need the fitted selector's ``get_support()``.
    """
    from skl2onnx import update_registered_converter
    from skl2onnx.operator_converters.feature_selection import (
        convert_sklearn_feature_selection,
    )
    from skl2onnx.shape_calculators.feature_selection import calculate_sklearn_select

    update_registered_converter(
        CorrelationThreshold,
        "HelixCorrelationThreshold",
        calculate_sklearn_select,
        convert_sklearn_feature_selection,
    )
