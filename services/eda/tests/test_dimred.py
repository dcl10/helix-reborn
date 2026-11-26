import numpy as np
import pandas as pd
from fastapi.testclient import TestClient

from helix_core.storage import Storage

PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
AUTH_HEADERS = {"Authorization": "Bearer some-token"}


def _put_dataset(storage: Storage, key: str, df: pd.DataFrame) -> None:
    storage.put_bytes(key, df.to_csv(index=False).encode("utf-8"), "text/csv")


def _clustered_dataset(n_per_cluster: int = 30) -> pd.DataFrame:
    rng = np.random.RandomState(0)
    cluster_a = rng.normal(loc=0.0, scale=0.1, size=(n_per_cluster, 4))
    cluster_b = rng.normal(loc=10.0, scale=0.1, size=(n_per_cluster, 4))
    data = np.vstack([cluster_a, cluster_b])
    labels = ["a"] * n_per_cluster + ["b"] * n_per_cluster
    df = pd.DataFrame(data, columns=["f1", "f2", "f3", "f4"])
    df["cluster"] = labels
    return df


def test_pca_returns_200_and_uploads_a_png(
    app_with_valid_token: TestClient, s3_storage: Storage
) -> None:
    df = _clustered_dataset()
    _put_dataset(s3_storage, "data.csv", df)

    response = app_with_valid_token.post(
        "/plots/pca",
        json={
            "dataset_key": "data.csv",
            "output_key": "experiments/e/eda/pca.png",
            "columns": ["f1", "f2", "f3", "f4"],
            "colour_column": "cluster",
        },
        headers=AUTH_HEADERS,
    )

    assert response.status_code == 200
    body = response.json()
    assert body["key"] == "experiments/e/eda/pca.png"
    assert body["title"] == "PCA"
    assert s3_storage.get_bytes("experiments/e/eda/pca.png").startswith(PNG_SIGNATURE)


def test_pca_explained_variance_appears_in_axis_labels() -> None:
    from helix_eda.plots import pca_plot
    from helix_eda.schemas import PcaRequest

    df = _clustered_dataset()
    request = PcaRequest(
        dataset_key="data.csv",
        output_key="out.png",
        columns=["f1", "f2", "f3", "f4"],
    )

    fig, _title = pca_plot(df, request)
    ax = fig.axes[0]

    assert ax.get_xlabel().startswith("PC1 (")
    assert ax.get_xlabel().endswith("%)")
    assert ax.get_ylabel().startswith("PC2 (")


def test_pca_unknown_column_returns_422(
    app_with_valid_token: TestClient, s3_storage: Storage
) -> None:
    df = _clustered_dataset()
    _put_dataset(s3_storage, "data.csv", df)

    response = app_with_valid_token.post(
        "/plots/pca",
        json={
            "dataset_key": "data.csv",
            "output_key": "experiments/e/eda/pca.png",
            "columns": ["f1", "missing"],
        },
        headers=AUTH_HEADERS,
    )

    assert response.status_code == 422


def test_pca_non_numeric_column_returns_422(
    app_with_valid_token: TestClient, s3_storage: Storage
) -> None:
    df = _clustered_dataset()
    _put_dataset(s3_storage, "data.csv", df)

    response = app_with_valid_token.post(
        "/plots/pca",
        json={
            "dataset_key": "data.csv",
            "output_key": "experiments/e/eda/pca.png",
            "columns": ["f1", "cluster"],
        },
        headers=AUTH_HEADERS,
    )

    assert response.status_code == 422


def test_tsne_returns_200_and_uploads_a_png(
    app_with_valid_token: TestClient, s3_storage: Storage
) -> None:
    df = _clustered_dataset(n_per_cluster=10)
    _put_dataset(s3_storage, "data.csv", df)

    response = app_with_valid_token.post(
        "/plots/tsne",
        json={
            "dataset_key": "data.csv",
            "output_key": "experiments/e/eda/tsne.png",
            "columns": ["f1", "f2", "f3", "f4"],
            "colour_column": "cluster",
            "perplexity": 5,
        },
        headers=AUTH_HEADERS,
    )

    assert response.status_code == 200
    body = response.json()
    assert body["key"] == "experiments/e/eda/tsne.png"
    assert s3_storage.get_bytes("experiments/e/eda/tsne.png").startswith(PNG_SIGNATURE)


def test_tsne_perplexity_greater_than_rows_returns_422(
    app_with_valid_token: TestClient, s3_storage: Storage
) -> None:
    df = _clustered_dataset(n_per_cluster=5)
    _put_dataset(s3_storage, "data.csv", df)

    response = app_with_valid_token.post(
        "/plots/tsne",
        json={
            "dataset_key": "data.csv",
            "output_key": "experiments/e/eda/tsne.png",
            "columns": ["f1", "f2", "f3", "f4"],
            "perplexity": 50,
        },
        headers=AUTH_HEADERS,
    )

    assert response.status_code == 422


def test_tsne_samples_down_to_5000_rows() -> None:
    from helix_eda.plots import _TSNE_MAX_ROWS, _prepare_dimensionality_reduction_data
    from helix_eda.schemas import TsneRequest

    df = pd.DataFrame(
        {"a": range(5100), "b": range(5100, 10200)},
        dtype=float,
    )
    request = TsneRequest(dataset_key="data.csv", output_key="out.png")
    data, _colours = _prepare_dimensionality_reduction_data(df, request)
    assert len(data) == 5100

    sampled = data.sample(n=_TSNE_MAX_ROWS, random_state=42)
    assert len(sampled) == _TSNE_MAX_ROWS
