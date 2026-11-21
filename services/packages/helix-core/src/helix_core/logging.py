"""JSON logging configuration for Helix Reborn's Python services.

Every service logs one JSON object per line to stdout, as required by
``docs/architecture.md`` ("JSON logs on stdout").
"""

import json
import logging
import sys
from datetime import UTC, datetime
from typing import Any

_EMPTY_RECORD = logging.LogRecord("", 0, "", 0, "", (), None)
_STANDARD_LOG_RECORD_ATTRS = frozenset(_EMPTY_RECORD.__dict__)

_HANDLER_MARKER = "_helix_json_handler"


class JsonFormatter(logging.Formatter):
    """Formats log records as single-line JSON objects.

    Each line contains ``timestamp`` (ISO 8601 UTC), ``level``, ``logger``, ``message``
    and ``service``, plus any fields passed via ``extra`` and, when the record carries
    exception information, an ``exception`` field with the formatted traceback.
    """

    def __init__(self, service: str) -> None:
        """Initialize the formatter.

        Args:
            service: Name of the service to stamp onto every log record.
        """
        super().__init__()
        self._service = service

    def format(self, record: logging.LogRecord) -> str:
        """Render a log record as a single JSON line.

        Args:
            record: The log record to format.

        Returns:
            The record as a JSON-encoded string, with no trailing newline.
        """
        payload: dict[str, Any] = {
            "timestamp": datetime.fromtimestamp(record.created, tz=UTC).isoformat(),
            "level": record.levelname,
            "logger": record.name,
            "message": record.getMessage(),
            "service": self._service,
        }
        for key, value in record.__dict__.items():
            if key not in _STANDARD_LOG_RECORD_ATTRS and key not in payload:
                payload[key] = value
        if record.exc_info:
            payload["exception"] = self.formatException(record.exc_info)
        return json.dumps(payload, default=str)


def configure_logging(level: str = "INFO", service: str = "helix") -> None:
    """Configure the root logger to write one JSON object per line to stdout.

    Safe to call more than once: it replaces any handler previously installed by this
    function instead of adding a duplicate.

    Args:
        level: Logging level name (e.g. ``"INFO"``, ``"DEBUG"``).
        service: Name of the service to stamp onto every log record.
    """
    root = logging.getLogger()
    root.setLevel(level)
    for existing in [h for h in root.handlers if getattr(h, _HANDLER_MARKER, False)]:
        root.removeHandler(existing)

    handler = logging.StreamHandler(sys.stdout)
    handler.setFormatter(JsonFormatter(service=service))
    setattr(handler, _HANDLER_MARKER, True)
    root.addHandler(handler)
