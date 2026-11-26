import pandas as pd
from fastapi.testclient import TestClient

from helix_core.storage import Storage

PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
AUTH_HEADERS = {"Authorization": "Bearer some-token"}


def _put_dataset(storage: Storage, key: str, df: pd.DataFrame) -> None:
    storage.put_bytes(key, df.to_csv(index=False).encode("utf-8"), "text/csv")


def test_target_distribution_numeric_returns_200_and_uploads_a_png(
    app_with_valid_token: TestClient, s3_storage: Storage
) -> None:
    df = pd.DataFrame({"price": [1.0, 2.0, 3.0, 4.0, 5.0]})
    _put_dataset(s3_storage, "data.csv", df)

    response = app_with_valid_token.post(
        "/plots/target-distribution",
        json={
            "dataset_key": "data.csv",
            "output_key": "experiments/e/eda/a.png",
            "target_column": "price",
        },
        headers=AUTH_HEADERS,
    )

    assert response.status_code == 200
    body = response.json()
    assert body["key"] == "experiments/e/eda/a.png"
    assert body["content_type"] == "image/png"
    assert body["title"] == "Target distribution: price"
    assert s3_storage.get_bytes("experiments/e/eda/a.png").startswith(PNG_SIGNATURE)


def test_target_distribution_non_numeric_gives_a_bar_chart_without_error(
    app_with_valid_token: TestClient, s3_storage: Storage
) -> None:
    df = pd.DataFrame({"site": ["A", "B", "A", "C"]})
    _put_dataset(s3_storage, "data.csv", df)

    response = app_with_valid_token.post(
        "/plots/target-distribution",
        json={
            "dataset_key": "data.csv",
            "output_key": "experiments/e/eda/a.png",
            "target_column": "site",
        },
        headers=AUTH_HEADERS,
    )

    assert response.status_code == 200
    assert s3_storage.get_bytes("experiments/e/eda/a.png").startswith(PNG_SIGNATURE)


def test_target_distribution_unknown_column_returns_422(
    app_with_valid_token: TestClient, s3_storage: Storage
) -> None:
    _put_dataset(s3_storage, "data.csv", pd.DataFrame({"price": [1.0, 2.0]}))

    response = app_with_valid_token.post(
        "/plots/target-distribution",
        json={
            "dataset_key": "data.csv",
            "output_key": "experiments/e/eda/a.png",
            "target_column": "missing",
        },
        headers=AUTH_HEADERS,
    )

    assert response.status_code == 422


def test_target_distribution_output_key_not_png_returns_422(
    app_with_valid_token: TestClient, s3_storage: Storage
) -> None:
    _put_dataset(s3_storage, "data.csv", pd.DataFrame({"price": [1.0, 2.0]}))

    response = app_with_valid_token.post(
        "/plots/target-distribution",
        json={
            "dataset_key": "data.csv",
            "output_key": "experiments/e/eda/a.jpg",
            "target_column": "price",
        },
        headers=AUTH_HEADERS,
    )

    assert response.status_code == 422


def test_correlation_heatmap_returns_200_and_uploads_a_png(
    app_with_valid_token: TestClient, s3_storage: Storage
) -> None:
    df = pd.DataFrame({"a": [1.0, 2.0, 3.0, 4.0], "b": [4.0, 3.0, 2.0, 1.0]})
    _put_dataset(s3_storage, "data.csv", df)

    response = app_with_valid_token.post(
        "/plots/correlation-heatmap",
        json={"dataset_key": "data.csv", "output_key": "experiments/e/eda/h.png"},
        headers=AUTH_HEADERS,
    )

    assert response.status_code == 200
    body = response.json()
    assert body["key"] == "experiments/e/eda/h.png"
    assert s3_storage.get_bytes("experiments/e/eda/h.png").startswith(PNG_SIGNATURE)


def test_correlation_heatmap_unknown_column_returns_422(
    app_with_valid_token: TestClient, s3_storage: Storage
) -> None:
    df = pd.DataFrame({"a": [1.0, 2.0, 3.0], "b": [3.0, 2.0, 1.0]})
    _put_dataset(s3_storage, "data.csv", df)

    response = app_with_valid_token.post(
        "/plots/correlation-heatmap",
        json={
            "dataset_key": "data.csv",
            "output_key": "experiments/e/eda/h.png",
            "columns": ["a", "missing"],
        },
        headers=AUTH_HEADERS,
    )

    assert response.status_code == 422


def test_pairplot_returns_200_and_uploads_a_png(
    app_with_valid_token: TestClient, s3_storage: Storage
) -> None:
    df = pd.DataFrame(
        {
            "a": [1.0, 2.0, 3.0, 4.0],
            "b": [4.0, 3.0, 2.0, 1.0],
            "c": [1.0, 1.0, 2.0, 2.0],
        }
    )
    _put_dataset(s3_storage, "data.csv", df)

    response = app_with_valid_token.post(
        "/plots/pairplot",
        json={
            "dataset_key": "data.csv",
            "output_key": "experiments/e/eda/p.png",
            "columns": ["a", "b"],
        },
        headers=AUTH_HEADERS,
    )

    assert response.status_code == 200
    body = response.json()
    assert body["key"] == "experiments/e/eda/p.png"
    assert s3_storage.get_bytes("experiments/e/eda/p.png").startswith(PNG_SIGNATURE)


def test_pairplot_with_eleven_columns_returns_422(
    app_with_valid_token: TestClient, s3_storage: Storage
) -> None:
    columns = [f"c{i}" for i in range(11)]
    df = pd.DataFrame({c: [1.0, 2.0, 3.0] for c in columns})
    _put_dataset(s3_storage, "data.csv", df)

    response = app_with_valid_token.post(
        "/plots/pairplot",
        json={
            "dataset_key": "data.csv",
            "output_key": "experiments/e/eda/p.png",
            "columns": columns,
        },
        headers=AUTH_HEADERS,
    )

    assert response.status_code == 422


def test_pairplot_samples_a_dataset_with_more_than_2000_rows(
    app_with_valid_token: TestClient, s3_storage: Storage
) -> None:
    df = pd.DataFrame({"a": range(2500), "b": range(2500, 5000)}, dtype=float)
    _put_dataset(s3_storage, "data.csv", df)

    response = app_with_valid_token.post(
        "/plots/pairplot",
        json={
            "dataset_key": "data.csv",
            "output_key": "experiments/e/eda/p.png",
            "columns": ["a", "b"],
        },
        headers=AUTH_HEADERS,
    )

    assert response.status_code == 200
    assert "sampled 2000 of 2500 rows" in response.json()["title"]
