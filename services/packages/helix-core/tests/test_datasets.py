import numpy as np
import pandas as pd
import pytest

from helix_core.datasets import load_csv, numeric_columns, select_features_target
from helix_core.errors import InvalidDataError
from helix_core.storage import Storage


def test_load_csv_reads_a_csv_put_into_moto(s3_storage: Storage) -> None:
    s3_storage.put_bytes("data.csv", b"a,b\n1,2\n3,4\n", "text/csv")

    df = load_csv(s3_storage, "data.csv")

    assert list(df.columns) == ["a", "b"]
    assert df.shape == (2, 2)


def test_load_csv_raises_on_an_empty_file(s3_storage: Storage) -> None:
    s3_storage.put_bytes("empty.csv", b"", "text/csv")

    with pytest.raises(InvalidDataError):
        load_csv(s3_storage, "empty.csv")


def test_load_csv_raises_on_a_non_csv_payload(s3_storage: Storage) -> None:
    s3_storage.put_bytes("binary.csv", b"\xff\xfe\x00\x01not csv", "text/csv")

    with pytest.raises(InvalidDataError):
        load_csv(s3_storage, "binary.csv")


def test_select_features_target_returns_the_right_shapes() -> None:
    df = pd.DataFrame({"a": [1, 2], "b": [3, 4], "y": [0, 1], "id": ["x1", "x2"]})

    x, y, ids = select_features_target(df, ["a", "b"], "y", "id")

    assert list(x.columns) == ["a", "b"]
    assert y.tolist() == [0, 1]
    assert ids.tolist() == ["x1", "x2"]


def test_select_features_target_without_target_or_id() -> None:
    df = pd.DataFrame({"a": [1, 2], "b": [3, 4]})

    x, y, ids = select_features_target(df, ["a", "b"], None)

    assert list(x.columns) == ["a", "b"]
    assert y is None
    assert ids is None


def test_select_features_target_missing_column_raises_with_its_name() -> None:
    df = pd.DataFrame({"a": [1, 2]})

    with pytest.raises(InvalidDataError, match="missing_col"):
        select_features_target(df, ["a", "missing_col"], None)


def test_select_features_target_text_feature_column_raises() -> None:
    df = pd.DataFrame({"a": ["x", "y"], "b": [1, 2]})

    with pytest.raises(InvalidDataError, match="a"):
        select_features_target(df, ["a", "b"], None)


def test_select_features_target_allows_nan_in_features() -> None:
    df = pd.DataFrame({"a": [1.0, np.nan], "b": [3, 4]})

    x, _, _ = select_features_target(df, ["a", "b"], None)

    assert x["a"].isna().sum() == 1


def test_select_features_target_target_in_features_raises() -> None:
    df = pd.DataFrame({"a": [1, 2], "y": [0, 1]})

    with pytest.raises(InvalidDataError):
        select_features_target(df, ["a", "y"], "y")


def test_numeric_columns_returns_only_numeric_columns() -> None:
    df = pd.DataFrame({"a": [1, 2], "b": ["x", "y"], "c": [1.0, 2.0]})

    assert numeric_columns(df) == ["a", "c"]
