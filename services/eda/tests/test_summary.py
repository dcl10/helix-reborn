import pandas as pd
from fastapi.testclient import TestClient

from helix_core.storage import Storage
from helix_eda.summary import summarise


def test_summarise_numeric_and_text_columns() -> None:
    df = pd.DataFrame(
        {
            "age": [20.0, 30.0, 40.0, 50.0],
            "site": ["A", "B", "A", "C"],
        }
    )

    result = summarise(df)

    assert result.row_count == 4
    assert result.column_count == 2

    age = next(c for c in result.columns if c.name == "age")
    assert age.is_numeric is True
    assert age.top_values is None
    assert age.mean == 35.0
    assert age.min == 20.0
    assert age.max == 50.0

    site = next(c for c in result.columns if c.name == "site")
    assert site.is_numeric is False
    assert site.mean is None
    assert site.top_values is not None
    counts = {tv.value: tv.count for tv in site.top_values}
    assert counts == {"A": 2, "B": 1, "C": 1}


def test_summarise_reports_missing_values_and_percentage() -> None:
    df = pd.DataFrame({"age": [20.0, None, 40.0, None]})

    result = summarise(df)

    age = result.columns[0]
    assert age.non_null == 2
    assert age.missing == 2
    assert age.missing_pct == 50.0


def test_summarise_all_nan_numeric_column_gives_null_stats() -> None:
    df = pd.DataFrame({"age": [float("nan"), float("nan"), float("nan")]})

    result = summarise(df)

    age = result.columns[0]
    assert age.is_numeric is True
    assert age.mean is None
    assert age.std is None
    assert age.min is None
    assert age.q25 is None
    assert age.median is None
    assert age.q75 is None
    assert age.max is None


def test_summary_route_with_moto_backed_storage_returns_the_documented_shape(
    app_with_valid_token: TestClient, s3_storage: Storage
) -> None:
    csv = "age,site\n21,A\n52,A\n53,B\n54,A\n55,B\n56,C\n57,A\n58,B\n59,A\n60,C\n"
    s3_storage.put_bytes("experiments/e/datasets/d/data.csv", csv.encode(), "text/csv")

    response = app_with_valid_token.post(
        "/summary",
        json={"dataset_key": "experiments/e/datasets/d/data.csv"},
        headers={"Authorization": "Bearer some-token"},
    )

    assert response.status_code == 200
    body = response.json()
    assert body["row_count"] == 10
    assert body["column_count"] == 2
    names = {c["name"] for c in body["columns"]}
    assert names == {"age", "site"}
    age = next(c for c in body["columns"] if c["name"] == "age")
    assert age["is_numeric"] is True
    assert age["top_values"] is None
    site = next(c for c in body["columns"] if c["name"] == "site")
    assert site["is_numeric"] is False
    assert site["top_values"] is not None


def test_summary_route_with_a_missing_key_returns_404(
    app_with_valid_token: TestClient,
) -> None:
    response = app_with_valid_token.post(
        "/summary",
        json={"dataset_key": "does/not/exist.csv"},
        headers={"Authorization": "Bearer some-token"},
    )

    assert response.status_code == 404
