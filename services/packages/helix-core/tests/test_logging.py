import json
import logging

from helix_core.logging import configure_logging


def test_configure_logging_writes_one_json_line_with_five_keys(capsys) -> None:
    configure_logging(service="test-service")
    logger = logging.getLogger("helix_core.tests")

    logger.info("hello world")

    line = capsys.readouterr().out.strip()
    payload = json.loads(line)
    assert set(payload) == {"timestamp", "level", "logger", "message", "service"}
    assert payload["level"] == "INFO"
    assert payload["logger"] == "helix_core.tests"
    assert payload["message"] == "hello world"
    assert payload["service"] == "test-service"


def test_configure_logging_includes_extra_fields(capsys) -> None:
    configure_logging(service="test-service")
    logger = logging.getLogger("helix_core.tests")

    logger.info("job update", extra={"job_id": "abc-123"})

    payload = json.loads(capsys.readouterr().out.strip())
    assert payload["job_id"] == "abc-123"


def test_configure_logging_includes_exception_on_error(capsys) -> None:
    configure_logging(service="test-service")
    logger = logging.getLogger("helix_core.tests")

    try:
        raise ValueError("boom")
    except ValueError:
        logger.exception("something failed")

    payload = json.loads(capsys.readouterr().out.strip())
    assert "exception" in payload
    assert "ValueError: boom" in payload["exception"]


def test_configure_logging_twice_does_not_duplicate_handlers() -> None:
    configure_logging(service="test-service")
    configure_logging(service="test-service")

    root = logging.getLogger()
    helix_handlers = [
        h for h in root.handlers if getattr(h, "_helix_json_handler", False)
    ]
    assert len(helix_handlers) == 1
