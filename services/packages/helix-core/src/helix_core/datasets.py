"""Dataset loading and validation for Helix Reborn's Python services.

Implements the upload and pipeline data rules from ``docs/architecture.md``
("Data rules (v1)"): CSV only, UTF-8, comma-separated, with a header row, max 50 MB,
and all feature columns numeric.
"""

import io

import pandas as pd

from helix_core.errors import InvalidDataError
from helix_core.storage import Storage

MAX_DATASET_BYTES = 50 * 1024 * 1024


def load_csv(storage: Storage, key: str) -> pd.DataFrame:
    """Read a CSV dataset from S3.

    Args:
        storage: The storage client to read from.
        key: The S3 key of the CSV object (UTF-8, comma-separated, with a header row).

    Returns:
        The parsed dataset.

    Raises:
        InvalidDataError: If the object isn't readable as a non-empty CSV.
    """
    raw = storage.get_bytes(key)
    try:
        df = pd.read_csv(io.BytesIO(raw), encoding="utf-8")
    except (pd.errors.EmptyDataError, pd.errors.ParserError, UnicodeDecodeError) as exc:
        raise InvalidDataError(f"Could not read {key} as a CSV: {exc}") from exc
    if df.empty:
        raise InvalidDataError(f"Dataset {key} has no rows")
    return df


def select_features_target(
    df: pd.DataFrame,
    feature_columns: list[str],
    target_column: str | None,
    id_column: str | None = None,
) -> tuple[pd.DataFrame, pd.Series | None, pd.Series | None]:
    """Split a dataset into features, target and IDs, validating columns along the way.

    Args:
        df: The dataset to select columns from.
        feature_columns: Names of the feature columns. Each must exist in ``df`` and
            be numeric (NaN is allowed).
        target_column: Name of the target column, or ``None`` if there isn't one (for
            example, when predicting). Must exist in ``df`` and not be a feature.
        id_column: Name of an identifier column to keep separately, or ``None``. Must
            exist in ``df`` and not be a feature.

    Returns:
        A tuple ``(X, y, ids)``: ``X`` holds the feature columns, ``y`` is the target
        column (or ``None``), and ``ids`` is the ID column (or ``None``).

    Raises:
        InvalidDataError: If a named column is missing, a feature column isn't
            numeric, or the target/ID column is also a feature column.
    """
    missing = [c for c in feature_columns if c not in df.columns]
    if target_column is not None and target_column not in df.columns:
        missing.append(target_column)
    if id_column is not None and id_column not in df.columns:
        missing.append(id_column)
    if missing:
        raise InvalidDataError(f"Missing column(s): {', '.join(missing)}")

    if target_column is not None and target_column in feature_columns:
        raise InvalidDataError(
            f"Target column '{target_column}' must not be a feature column"
        )
    if id_column is not None and id_column in feature_columns:
        raise InvalidDataError(f"ID column '{id_column}' must not be a feature column")

    non_numeric: list[str] = []
    for column in feature_columns:
        try:
            pd.to_numeric(df[column], errors="raise")
        except (ValueError, TypeError):
            non_numeric.append(column)
            continue
        if not pd.api.types.is_numeric_dtype(df[column]):
            non_numeric.append(column)
    if non_numeric:
        names = ", ".join(non_numeric)
        raise InvalidDataError(f"Non-numeric feature column(s): {names}")

    x = df[feature_columns].copy()
    y = df[target_column].copy() if target_column is not None else None
    ids = df[id_column].copy() if id_column is not None else None
    return x, y, ids


def numeric_columns(df: pd.DataFrame) -> list[str]:
    """Return the names of columns whose dtype is numeric.

    Args:
        df: The dataset to inspect.

    Returns:
        Column names, in the dataset's column order, whose dtype is numeric.
    """
    return [c for c in df.columns if pd.api.types.is_numeric_dtype(df[c])]
