"""A minimal HTTP health endpoint for Helix Reborn's Python workers.

Workers have no other reason to listen on a port, so this implements just enough of
HTTP/1.1 with the standard library to answer ``GET /health`` for container health
checks, without adding a web framework dependency.
"""

import asyncio
import json
import logging
from collections.abc import Callable

logger = logging.getLogger(__name__)

_REQUEST_LINE_TIMEOUT_SECONDS = 10


async def start_health_server(
    port: int, is_ready: Callable[[], bool], version: str
) -> asyncio.base_events.Server:
    """Start a background HTTP server answering health checks.

    ``GET /health`` returns ``200 {"status": "ok", "version": version}`` when
    ``is_ready()`` is true, otherwise ``503 {"status": "unavailable", "version":
    version}``. Any other path returns ``404``.

    Args:
        port: The TCP port to listen on. Use ``0`` to let the OS pick a free port
            (its socket's actual port can be read from the returned server).
        is_ready: Called on each request to decide readiness. Should be cheap and
            non-blocking.
        version: The service version to report in the body.

    Returns:
        The running :class:`asyncio.base_events.Server`. Callers should keep a
        reference and call ``close()`` (and await ``wait_closed()``) on shutdown.
    """

    async def handle_client(
        reader: asyncio.StreamReader, writer: asyncio.StreamWriter
    ) -> None:
        try:
            request_line = await asyncio.wait_for(
                reader.readline(), timeout=_REQUEST_LINE_TIMEOUT_SECONDS
            )
            while True:
                header_line = await asyncio.wait_for(
                    reader.readline(), timeout=_REQUEST_LINE_TIMEOUT_SECONDS
                )
                if header_line in (b"\r\n", b"\n", b""):
                    break

            parts = request_line.decode("latin-1").strip().split(" ")
            path = parts[1] if len(parts) >= 2 else ""

            if path == "/health":
                ready = is_ready()
                status_line = "200 OK" if ready else "503 Service Unavailable"
                body = json.dumps(
                    {"status": "ok" if ready else "unavailable", "version": version}
                ).encode("utf-8")
            else:
                status_line = "404 Not Found"
                body = b""

            response = (
                f"HTTP/1.1 {status_line}\r\n"
                f"Content-Type: application/json\r\n"
                f"Content-Length: {len(body)}\r\n"
                "Connection: close\r\n"
                "\r\n"
            ).encode("latin-1") + body
            writer.write(response)
            await writer.drain()
        except (TimeoutError, ConnectionError, asyncio.IncompleteReadError):
            logger.debug("Health check connection closed early", exc_info=True)
        finally:
            writer.close()

    server = await asyncio.start_server(handle_client, host="0.0.0.0", port=port)
    return server
