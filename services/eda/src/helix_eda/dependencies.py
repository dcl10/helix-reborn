"""FastAPI dependencies for the Helix EDA service: auth and storage."""

from functools import lru_cache
from typing import Annotated, Any

from fastapi import Depends, Header, HTTPException

from helix_core.auth import JwtValidator, TokenValidationError
from helix_core.settings import get_settings
from helix_core.storage import Storage

_UNAUTHORIZED_HEADERS = {"WWW-Authenticate": "Bearer"}


@lru_cache
def get_validator() -> JwtValidator:
    """Return the process-wide :class:`JwtValidator`, built from settings.

    Returns:
        A cached :class:`JwtValidator` configured with the Keycloak issuer, JWKS URL
        and EDA audience from :func:`helix_core.settings.get_settings`.
    """
    settings = get_settings()
    return JwtValidator(
        issuer=settings.keycloak_issuer,
        jwks_url=settings.jwks_url,
        audience=settings.eda_audience,
    )


@lru_cache
def get_storage() -> Storage:
    """Return the process-wide :class:`Storage`, built from settings.

    An ``app.dependency_overrides`` target so tests can substitute a moto-backed or
    fake storage instance.

    Returns:
        A cached :class:`Storage` bound to the configured bucket.
    """
    return Storage.from_settings(get_settings())


def require_service_token(
    validator: Annotated[JwtValidator, Depends(get_validator)],
    authorization: Annotated[str | None, Header()] = None,
) -> dict[str, Any]:
    """Require a valid Keycloak bearer token, audienced to this service.

    Args:
        authorization: The raw ``Authorization`` header value, expected to be
            ``Bearer <token>``. Never logged.
        validator: The validator to check the token against. Overridable via
            ``app.dependency_overrides[get_validator]`` in tests.

    Returns:
        The token's claims.

    Raises:
        HTTPException: 401, with a ``WWW-Authenticate: Bearer`` header, if the header
            is missing, malformed, or the token fails validation.
    """
    if authorization is None or not authorization.startswith("Bearer "):
        raise HTTPException(
            status_code=401,
            detail="Missing or malformed Authorization header",
            headers=_UNAUTHORIZED_HEADERS,
        )
    token = authorization.removeprefix("Bearer ")
    try:
        return validator.validate(token)
    except TokenValidationError as exc:
        raise HTTPException(
            status_code=401, detail=str(exc), headers=_UNAUTHORIZED_HEADERS
        ) from exc
