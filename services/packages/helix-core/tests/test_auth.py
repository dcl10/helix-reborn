import time
from dataclasses import dataclass
from typing import Any

import jwt
import pytest
from cryptography.hazmat.primitives.asymmetric import rsa

from helix_core.auth import JwtValidator, TokenValidationError

ISSUER = "http://localhost:8080/realms/helix"
AUDIENCE = "helix-eda"


@dataclass
class _FakeSigningKey:
    key: Any


class _FakeJwksClient:
    """Stands in for jwt.PyJWKClient, returning a fixed signing key."""

    def __init__(self, key: Any) -> None:
        self._key = key

    def get_signing_key_from_jwt(self, token: str) -> _FakeSigningKey:
        return _FakeSigningKey(self._key)


def _rsa_key_pair() -> tuple[rsa.RSAPrivateKey, rsa.RSAPublicKey]:
    private_key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    return private_key, private_key.public_key()


def _make_token(
    private_key: rsa.RSAPrivateKey,
    *,
    issuer: str = ISSUER,
    audience: str = AUDIENCE,
    expires_in: float = 300,
    algorithm: str = "RS256",
) -> str:
    now = int(time.time())
    claims = {
        "iss": issuer,
        "aud": audience,
        "iat": now,
        "exp": now + expires_in,
        "sub": "service-account-helix-api",
    }
    return jwt.encode(claims, private_key, algorithm=algorithm)


@pytest.fixture
def key_pair() -> tuple[rsa.RSAPrivateKey, rsa.RSAPublicKey]:
    return _rsa_key_pair()


@pytest.fixture
def validator(
    key_pair: tuple[rsa.RSAPrivateKey, rsa.RSAPublicKey],
) -> JwtValidator:
    _, public_key = key_pair
    return JwtValidator(
        issuer=ISSUER,
        jwks_url="https://unused.example/certs",
        audience=AUDIENCE,
        jwks_client=_FakeJwksClient(public_key),
    )


def test_validate_returns_claims_for_a_valid_token(
    validator: JwtValidator,
    key_pair: tuple[rsa.RSAPrivateKey, rsa.RSAPublicKey],
) -> None:
    private_key, _ = key_pair
    token = _make_token(private_key)

    claims = validator.validate(token)

    assert claims["iss"] == ISSUER
    assert claims["aud"] == AUDIENCE


def test_validate_raises_for_wrong_audience(
    validator: JwtValidator,
    key_pair: tuple[rsa.RSAPrivateKey, rsa.RSAPublicKey],
) -> None:
    private_key, _ = key_pair
    token = _make_token(private_key, audience="someone-else")

    with pytest.raises(TokenValidationError):
        validator.validate(token)


def test_validate_raises_for_wrong_issuer(
    validator: JwtValidator,
    key_pair: tuple[rsa.RSAPrivateKey, rsa.RSAPublicKey],
) -> None:
    private_key, _ = key_pair
    token = _make_token(private_key, issuer="http://attacker.example/realms/helix")

    with pytest.raises(TokenValidationError):
        validator.validate(token)


def test_validate_raises_for_an_expired_token(
    validator: JwtValidator,
    key_pair: tuple[rsa.RSAPrivateKey, rsa.RSAPublicKey],
) -> None:
    private_key, _ = key_pair
    token = _make_token(private_key, expires_in=-120)

    with pytest.raises(TokenValidationError):
        validator.validate(token)


def test_validate_raises_for_a_bad_signature(
    validator: JwtValidator,
) -> None:
    other_private_key, _ = _rsa_key_pair()
    token = _make_token(other_private_key)

    with pytest.raises(TokenValidationError):
        validator.validate(token)


def test_validate_raises_for_an_hs256_token(
    validator: JwtValidator,
    key_pair: tuple[rsa.RSAPrivateKey, rsa.RSAPublicKey],
) -> None:
    now = int(time.time())
    claims = {
        "iss": ISSUER,
        "aud": AUDIENCE,
        "iat": now,
        "exp": now + 300,
    }
    token = jwt.encode(claims, "some-shared-secret", algorithm="HS256")

    with pytest.raises(TokenValidationError):
        validator.validate(token)


def test_validate_does_not_leak_the_token_in_the_error_message(
    validator: JwtValidator,
    key_pair: tuple[rsa.RSAPrivateKey, rsa.RSAPublicKey],
) -> None:
    private_key, _ = key_pair
    token = _make_token(private_key, audience="someone-else")

    with pytest.raises(TokenValidationError) as exc_info:
        validator.validate(token)

    assert token not in str(exc_info.value)
