import pytest
from pydantic import ValidationError

from helix_core.settings import HelixSettings, get_settings


def test_settings_defaults_match_the_table() -> None:
    settings = HelixSettings()

    assert settings.s3_endpoint_url == "http://localhost:9000"
    assert settings.s3_region == "us-east-1"
    assert settings.s3_access_key.get_secret_value() == "helix"
    assert settings.s3_secret_key.get_secret_value() == "dev_password"
    assert settings.s3_bucket == "helix"
    assert (
        settings.rabbitmq_url.get_secret_value()
        == "amqp://helix:dev_password@localhost:5672/"
    )
    assert settings.worker_cpus == 1
    assert settings.health_port == 8080
    assert settings.keycloak_issuer == "http://localhost:8080/realms/helix"
    assert settings.keycloak_jwks_url is None
    assert settings.eda_audience == "helix-eda"
    assert settings.log_level == "INFO"
    assert settings.version == "dev"


def test_settings_env_overrides_work(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("HELIX_S3_BUCKET", "x")

    settings = HelixSettings()

    assert settings.s3_bucket == "x"


def test_settings_jwks_url_is_derived_when_unset() -> None:
    settings = HelixSettings(keycloak_issuer="https://auth.example.com/realms/helix")

    assert (
        settings.jwks_url
        == "https://auth.example.com/realms/helix/protocol/openid-connect/certs"
    )


def test_settings_jwks_url_is_used_as_is_when_set() -> None:
    settings = HelixSettings(keycloak_jwks_url="https://example.com/certs")

    assert settings.jwks_url == "https://example.com/certs"


def test_settings_worker_cpus_zero_is_rejected() -> None:
    with pytest.raises(ValidationError):
        HelixSettings(worker_cpus=0)


def test_get_settings_is_cached() -> None:
    assert get_settings() is get_settings()
