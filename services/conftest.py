"""Shared pytest configuration for the whole uv workspace.

Skips ``@pytest.mark.integration`` tests unless ``HELIX_INTEGRATION=1`` is set, per
``CLAUDE.md`` and ``docs/architecture.md``: those tests need a real RabbitMQ, MinIO or
Keycloak and must not run by default.
"""

import os

import pytest


def pytest_collection_modifyitems(
    config: pytest.Config, items: list[pytest.Item]
) -> None:
    """Skip integration tests unless explicitly enabled.

    Args:
        config: The pytest configuration (unused).
        items: The collected test items, mutated in place.
    """
    if os.environ.get("HELIX_INTEGRATION") == "1":
        return
    skip_integration = pytest.mark.skip(reason="set HELIX_INTEGRATION=1 to run")
    for item in items:
        if "integration" in item.keywords:
            item.add_marker(skip_integration)
