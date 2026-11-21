import json

import httpx
import pytest

from helix_core.health import start_health_server


async def _get(port: int, path: str) -> httpx.Response:
    async with httpx.AsyncClient(timeout=5) as client:
        return await client.get(f"http://127.0.0.1:{port}{path}")


@pytest.fixture
async def ready_flag() -> list[bool]:
    return [True]


async def _start(ready_flag: list[bool]) -> tuple[int, object]:
    server = await start_health_server(0, lambda: ready_flag[0], "1.2.3")
    port = server.sockets[0].getsockname()[1]
    return port, server


async def test_health_returns_200_when_ready(ready_flag: list[bool]) -> None:
    port, server = await _start(ready_flag)
    try:
        response = await _get(port, "/health")
        assert response.status_code == 200
        assert json.loads(response.text) == {"status": "ok", "version": "1.2.3"}
    finally:
        server.close()
        await server.wait_closed()


async def test_health_returns_503_when_not_ready(ready_flag: list[bool]) -> None:
    ready_flag[0] = False
    port, server = await _start(ready_flag)
    try:
        response = await _get(port, "/health")
        assert response.status_code == 503
        assert json.loads(response.text) == {
            "status": "unavailable",
            "version": "1.2.3",
        }
    finally:
        server.close()
        await server.wait_closed()


async def test_other_paths_return_404(ready_flag: list[bool]) -> None:
    port, server = await _start(ready_flag)
    try:
        response = await _get(port, "/other")
        assert response.status_code == 404
    finally:
        server.close()
        await server.wait_closed()
