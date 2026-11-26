import numpy as np
import pandas as pd
from fastapi.testclient import TestClient

from helix_core.storage import Storage
from helix_eda.stats import normality_table

AUTH_HEADERS = {"Authorization": "Bearer some-token"}


def _put_dataset(storage: Storage, key: str, df: pd.DataFrame) -> None:
    storage.put_bytes(key, df.to_csv(index=False).encode("utf-8"), "text/csv")


def test_normality_table_normal_sample_is_normal_at_0_05() -> None:
    rng = np.random.RandomState(42)
    df = pd.DataFrame({"x": rng.normal(loc=0.0, scale=1.0, size=500)})

    rows = normality_table(df, None)

    assert len(rows) == 1
    assert rows[0].column == "x"
    assert rows[0].n == 500
    assert rows[0].normal_at_0_05 is True


def test_normality_table_exponential_sample_is_not_normal_at_0_05() -> None:
    rng = np.random.RandomState(42)
    df = pd.DataFrame({"x": rng.exponential(scale=1.0, size=500)})

    rows = normality_table(df, None)

    assert len(rows) == 1
    assert rows[0].normal_at_0_05 is False


def test_normality_table_skips_short_columns() -> None:
    df = pd.DataFrame(
        {
            "short": [1.0, 2.0, np.nan, np.nan, np.nan],
            "long": [1.0, 2.0, 3.0, 4.0, 5.0],
        }
    )

    rows = normality_table(df, None)

    assert [row.column for row in rows] == ["long"]


def test_normality_table_unknown_column_raises() -> None:
    from helix_core.errors import InvalidSpecError

    df = pd.DataFrame({"x": [1.0, 2.0, 3.0]})

    try:
        normality_table(df, ["missing"])
        raised = False
    except InvalidSpecError:
        raised = True
    assert raised


def test_normality_endpoint_writes_csv(
    app_with_valid_token: TestClient, s3_storage: Storage
) -> None:
    rng = np.random.RandomState(42)
    df = pd.DataFrame({"x": rng.normal(size=500)})
    _put_dataset(s3_storage, "data.csv", df)

    response = app_with_valid_token.post(
        "/tables/normality",
        json={"dataset_key": "data.csv", "output_key": "experiments/e/eda/n.csv"},
        headers=AUTH_HEADERS,
    )

    assert response.status_code == 200
    body = response.json()
    assert body["key"] == "experiments/e/eda/n.csv"
    assert body["content_type"] == "text/csv"
    assert body["title"] == "Normality tests"
    assert len(body["rows"]) == 1
    assert body["rows"][0]["column"] == "x"
    csv_bytes = s3_storage.get_bytes("experiments/e/eda/n.csv")
    assert csv_bytes.startswith(b"column,")
    assert body["size_bytes"] == len(csv_bytes)


def test_normality_output_key_not_csv_returns_422(
    app_with_valid_token: TestClient, s3_storage: Storage
) -> None:
    _put_dataset(s3_storage, "data.csv", pd.DataFrame({"x": [1.0, 2.0, 3.0]}))

    response = app_with_valid_token.post(
        "/tables/normality",
        json={"dataset_key": "data.csv", "output_key": "experiments/e/eda/n.png"},
        headers=AUTH_HEADERS,
    )

    assert response.status_code == 422


def test_normality_non_numeric_column_returns_422(
    app_with_valid_token: TestClient, s3_storage: Storage
) -> None:
    df = pd.DataFrame({"x": [1.0, 2.0, 3.0], "site": ["a", "b", "c"]})
    _put_dataset(s3_storage, "data.csv", df)

    response = app_with_valid_token.post(
        "/tables/normality",
        json={
            "dataset_key": "data.csv",
            "output_key": "experiments/e/eda/n.csv",
            "columns": ["site"],
        },
        headers=AUTH_HEADERS,
    )

    assert response.status_code == 422
