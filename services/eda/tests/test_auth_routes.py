from fastapi.testclient import TestClient

from helix_core.storage import Storage


def test_health_without_a_token_returns_200(app_with_invalid_token: TestClient) -> None:
    response = app_with_invalid_token.get("/health")

    assert response.status_code == 200
    assert response.json()["status"] == "ok"


def test_summary_without_a_token_returns_401_with_www_authenticate(
    app_with_valid_token: TestClient,
) -> None:
    response = app_with_valid_token.post("/summary", json={"dataset_key": "x"})

    assert response.status_code == 401
    assert response.headers["www-authenticate"] == "Bearer"


def test_summary_with_a_malformed_header_returns_401(
    app_with_valid_token: TestClient,
) -> None:
    response = app_with_valid_token.post(
        "/summary",
        json={"dataset_key": "x"},
        headers={"Authorization": "NotBearer abc"},
    )

    assert response.status_code == 401
    assert response.headers["www-authenticate"] == "Bearer"


def test_summary_with_an_invalid_token_returns_401(
    app_with_invalid_token: TestClient,
) -> None:
    response = app_with_invalid_token.post(
        "/summary",
        json={"dataset_key": "x"},
        headers={"Authorization": "Bearer some-token"},
    )

    assert response.status_code == 401
    assert response.headers["www-authenticate"] == "Bearer"


def test_summary_with_a_valid_token_returns_200(
    app_with_valid_token: TestClient, s3_storage: Storage
) -> None:
    s3_storage.put_bytes("data.csv", b"a,b\n1,2\n3,4\n", "text/csv")

    response = app_with_valid_token.post(
        "/summary",
        json={"dataset_key": "data.csv"},
        headers={"Authorization": "Bearer some-token"},
    )

    assert response.status_code == 200
    body = response.json()
    assert body["row_count"] == 2
    assert body["column_count"] == 2
