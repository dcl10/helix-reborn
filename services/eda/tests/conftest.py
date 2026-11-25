from typing import Any

import boto3
import pytest
from fastapi.testclient import TestClient
from moto import mock_aws

from helix_core.storage import Storage
from helix_eda.app import create_app
from helix_eda.dependencies import get_storage, get_validator

BUCKET = "helix"
VALID_CLAIMS = {"sub": "service-account-helix-api", "aud": "helix-eda"}


class _FakeValidator:
    """A validator that either always succeeds or always rejects."""

    def __init__(self, *, valid: bool) -> None:
        self._valid = valid

    def validate(self, token: str) -> dict[str, Any]:
        if not self._valid:
            from helix_core.auth import TokenValidationError

            raise TokenValidationError("invalid token")
        return VALID_CLAIMS


@pytest.fixture
def s3_storage() -> Storage:
    with mock_aws():
        client = boto3.client("s3", region_name="us-east-1")
        client.create_bucket(Bucket=BUCKET)
        yield Storage(client, BUCKET)


@pytest.fixture
def app_with_valid_token(s3_storage: Storage) -> TestClient:
    app = create_app()
    app.dependency_overrides[get_validator] = lambda: _FakeValidator(valid=True)
    app.dependency_overrides[get_storage] = lambda: s3_storage
    return TestClient(app)


@pytest.fixture
def app_with_invalid_token(s3_storage: Storage) -> TestClient:
    app = create_app()
    app.dependency_overrides[get_validator] = lambda: _FakeValidator(valid=False)
    app.dependency_overrides[get_storage] = lambda: s3_storage
    return TestClient(app)
