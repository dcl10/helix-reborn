"""Process configuration for Helix Reborn's Python services.

All settings are read from ``HELIX_*`` environment variables, as documented in
``docs/architecture.md`` ("Configuration"). No code outside this module should read
``os.environ`` directly.
"""

from functools import lru_cache

from pydantic import Field, SecretStr
from pydantic_settings import BaseSettings, SettingsConfigDict


class HelixSettings(BaseSettings):
    """All Helix service settings, read from HELIX_* environment variables."""

    model_config = SettingsConfigDict(env_prefix="HELIX_", extra="ignore")

    s3_endpoint_url: str | None = "http://localhost:9000"
    s3_region: str = "us-east-1"
    s3_access_key: SecretStr = SecretStr("helix")
    s3_secret_key: SecretStr = SecretStr("dev_password")
    s3_bucket: str = "helix"
    rabbitmq_url: SecretStr = SecretStr("amqp://helix:dev_password@localhost:5672/")
    worker_cpus: int = Field(default=1, ge=1)
    health_port: int = 8080
    keycloak_issuer: str = "http://localhost:8080/realms/helix"
    keycloak_jwks_url: str | None = None
    eda_audience: str = "helix-eda"
    log_level: str = "INFO"
    version: str = "dev"

    @property
    def jwks_url(self) -> str:
        """Return the Keycloak JWKS URL.

        Returns:
            ``keycloak_jwks_url`` if set, otherwise the URL derived from
            ``keycloak_issuer``.
        """
        if self.keycloak_jwks_url is not None:
            return self.keycloak_jwks_url
        return f"{self.keycloak_issuer}/protocol/openid-connect/certs"


@lru_cache
def get_settings() -> HelixSettings:
    """Return the process-wide settings.

    Returns:
        The cached :class:`HelixSettings` instance for this process.
    """
    return HelixSettings()
