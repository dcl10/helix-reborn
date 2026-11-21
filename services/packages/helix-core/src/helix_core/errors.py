"""Expected, user-facing error types shared by Helix Reborn's Python services.

These are reported as job failures (see ``JobEvent.error`` in ``docs/architecture.md``),
not crashes: workers catch them, publish a `failed` event and ack the message, rather
than letting the job fall into the dead-letter queue.
"""


class HelixError(Exception):
    """Base class for expected, user-facing errors.

    Reported as job failures, not crashes.
    """


class InvalidDataError(HelixError):
    """The dataset doesn't satisfy what the job asked for.

    For example a missing column, a non-numeric feature, or too few rows.
    """


class InvalidSpecError(HelixError):
    """A pipeline spec or job option is invalid."""


class StorageError(HelixError):
    """An object is missing or unreadable in S3."""
