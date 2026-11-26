"""JWT validation for Helix Reborn's Python services.

Implements the service-token checks described in ``docs/architecture.md`` ("Auth"):
the EDA service (and, later, the workers) validate Keycloak-issued bearer tokens by
signature, issuer, audience and expiry, fetching signing keys from the realm's JWKS
endpoint.
"""

from typing import Any

import jwt


class TokenValidationError(Exception):
    """Raised when a bearer token fails validation.

    The message never includes the token itself.
    """


class JwtValidator:
    """Validates Keycloak access tokens.

    Checks the RS256 signature against the realm's JWKS, the issuer, the audience and
    expiry (with 30 seconds of leeway for clock skew between services).
    """

    def __init__(
        self,
        issuer: str,
        jwks_url: str,
        audience: str,
        jwks_client: "jwt.PyJWKClient | None" = None,
    ) -> None:
        """Initialize the validator.

        Args:
            issuer: The expected ``iss`` claim, Keycloak's public-facing realm URL.
            jwks_url: The URL to fetch signing keys from. Kept separate from
                ``issuer`` because, in Docker, the issuer is the public URL but the
                JWKS must be fetched from the internal Keycloak hostname.
            audience: The expected ``aud`` claim.
            jwks_client: A pre-built JWKS client, or ``None`` to build one with
                ``cache_keys=True`` and a 300-second cache lifespan. Tests inject a
                fake here instead of fetching real keys over HTTP.
        """
        self._issuer = issuer
        self._audience = audience
        self._jwks_client = jwks_client or jwt.PyJWKClient(
            jwks_url, cache_keys=True, lifespan=300
        )

    def validate(self, token: str) -> dict[str, Any]:
        """Validate a bearer token and return its claims.

        Args:
            token: The raw JWT, without the ``Bearer`` prefix.

        Returns:
            The token's claims.

        Raises:
            TokenValidationError: If the token's signature, issuer, audience or
                expiry is invalid, or its signing key can't be found. The message
                never includes the token itself.
        """
        try:
            signing_key = self._jwks_client.get_signing_key_from_jwt(token)
            claims: dict[str, Any] = jwt.decode(
                token,
                signing_key.key,
                algorithms=["RS256"],
                audience=self._audience,
                issuer=self._issuer,
                leeway=30,
                options={"require": ["exp", "iat", "iss", "aud"]},
            )
        except jwt.PyJWTError as exc:
            error_type = exc.__class__.__name__
            raise TokenValidationError(f"Invalid token: {error_type}") from exc
        return claims
