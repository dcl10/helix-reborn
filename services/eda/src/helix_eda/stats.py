"""Normality testing for the EDA service's table endpoints.

Ported from the original Helix's ``helix/services/statistical_tests.py``: Shapiro-Wilk
and Kolmogorov-Smirnov (fit to a normal distribution) per numeric column.
"""

import numpy as np
import pandas as pd
from scipy import stats

from helix_core.datasets import numeric_columns
from helix_core.errors import InvalidSpecError
from helix_eda.schemas import NormalityRow

_MIN_NON_NULL = 3
_SHAPIRO_MAX_SAMPLE = 5000
_SHAPIRO_RANDOM_STATE = 42
_NORMAL_ALPHA = 0.05


def normality_table(df: pd.DataFrame, columns: list[str] | None) -> list[NormalityRow]:
    """Run Shapiro-Wilk and Kolmogorov-Smirnov normality tests on numeric columns.

    Columns with fewer than 3 non-null values are skipped. The Shapiro-Wilk test is
    run on at most 5000 values, sampled with ``random_state=42``; the
    Kolmogorov-Smirnov test runs against a normal distribution fitted to the column
    (``scipy.stats.norm.fit``), using every non-null value.

    Args:
        df: The dataset to test.
        columns: Columns to test, or ``None`` for all numeric columns.

    Returns:
        One row per tested column, in column order. Columns with fewer than 3
        non-null values are omitted.

    Raises:
        InvalidSpecError: If a requested column doesn't exist or isn't numeric.
    """
    columns = columns if columns is not None else numeric_columns(df)
    _check_numeric_columns(df, columns)

    rows = []
    for column in columns:
        values = df[column].dropna().to_numpy(dtype=float)
        if len(values) < _MIN_NON_NULL:
            continue
        rows.append(_test_column(column, values))
    return rows


def _test_column(column: str, values: np.ndarray) -> NormalityRow:
    shapiro_sample = values
    if len(shapiro_sample) > _SHAPIRO_MAX_SAMPLE:
        rng = np.random.RandomState(_SHAPIRO_RANDOM_STATE)
        shapiro_sample = rng.choice(
            shapiro_sample, size=_SHAPIRO_MAX_SAMPLE, replace=False
        )
    shapiro_statistic, shapiro_p = stats.shapiro(shapiro_sample)

    norm_params = stats.norm.fit(values)
    ks_statistic, ks_p = stats.kstest(values, stats.norm(*norm_params).cdf)

    return NormalityRow(
        column=column,
        n=len(values),
        shapiro_statistic=float(shapiro_statistic),
        shapiro_p=float(shapiro_p),
        ks_statistic=float(ks_statistic),
        ks_p=float(ks_p),
        normal_at_0_05=shapiro_p > _NORMAL_ALPHA and ks_p > _NORMAL_ALPHA,
    )


def _check_numeric_columns(df: pd.DataFrame, columns: list[str]) -> None:
    """Validate that every column exists and is numeric.

    Args:
        df: The dataset the columns are drawn from.
        columns: The column names to validate.

    Raises:
        InvalidSpecError: If a column is unknown or non-numeric.
    """
    unknown = [c for c in columns if c not in df.columns]
    if unknown:
        raise InvalidSpecError(f"Unknown column(s): {', '.join(unknown)}")
    non_numeric = [c for c in columns if not pd.api.types.is_numeric_dtype(df[c])]
    if non_numeric:
        raise InvalidSpecError(f"Non-numeric column(s): {', '.join(non_numeric)}")
