"""Dataset summary statistics, computed over an in-memory dataframe."""

import math

import pandas as pd

from helix_eda.schemas import ColumnSummary, DatasetSummary, TopValue

_SIGNIFICANT_DIGITS = 6
_MAX_TOP_VALUES = 10


def summarise(df: pd.DataFrame) -> DatasetSummary:
    """Compute summary statistics for every column of a dataset.

    Numeric columns get descriptive statistics (mean, std, quartiles, etc.), rounded
    to 6 significant digits, with ``null`` for any statistic that can't be computed
    (for example, an all-missing column). Non-numeric columns get the 10 most common
    values and their counts instead.

    Args:
        df: The dataset to summarise.

    Returns:
        The dataset's row/column counts and per-column summaries, in column order.
    """
    columns = [_summarise_column(df[name]) for name in df.columns]
    return DatasetSummary(
        row_count=len(df),
        column_count=len(df.columns),
        columns=columns,
    )


def _summarise_column(series: pd.Series) -> ColumnSummary:
    is_numeric = pd.api.types.is_numeric_dtype(series)
    non_null = int(series.notna().sum())
    missing = int(series.isna().sum())
    row_count = len(series)
    missing_pct = _round_sig((missing / row_count) * 100 if row_count else 0.0)

    stats = _numeric_stats(series) if is_numeric else _empty_numeric_stats()
    top_values = None if is_numeric else _top_values(series)

    return ColumnSummary(
        name=str(series.name),
        dtype=str(series.dtype),
        is_numeric=is_numeric,
        non_null=non_null,
        missing=missing,
        missing_pct=missing_pct if missing_pct is not None else 0.0,
        unique=int(series.nunique(dropna=True)),
        top_values=top_values,
        **stats,
    )


def _numeric_stats(series: pd.Series) -> dict[str, float | None]:
    non_null = series.dropna()
    if non_null.empty:
        return _empty_numeric_stats()
    return {
        "mean": _round_sig(non_null.mean()),
        "std": _round_sig(non_null.std()) if len(non_null) > 1 else None,
        "min": _round_sig(non_null.min()),
        "q25": _round_sig(non_null.quantile(0.25)),
        "median": _round_sig(non_null.median()),
        "q75": _round_sig(non_null.quantile(0.75)),
        "max": _round_sig(non_null.max()),
    }


def _empty_numeric_stats() -> dict[str, float | None]:
    return {
        "mean": None,
        "std": None,
        "min": None,
        "q25": None,
        "median": None,
        "q75": None,
        "max": None,
    }


def _top_values(series: pd.Series) -> list[TopValue]:
    counts = series.dropna().astype(str).value_counts().head(_MAX_TOP_VALUES)
    return [TopValue(value=value, count=int(count)) for value, count in counts.items()]


def _round_sig(value: float | None) -> float | None:
    """Round a value to 6 significant digits, mapping NaN/None to None."""
    if value is None or (isinstance(value, float) and math.isnan(value)):
        return None
    value = float(value)
    if value == 0:
        return 0.0
    digits = _SIGNIFICANT_DIGITS - math.floor(math.log10(abs(value))) - 1
    return round(value, digits)
