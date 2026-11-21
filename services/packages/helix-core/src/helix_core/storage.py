"""S3 object storage access for Helix Reborn's Python services.

One bucket holds datasets, plots, tables and models, as laid out in
``docs/architecture.md`` ("Storage layout (S3)"). Locally this bucket is served by
RustFS/MinIO; in production it is DigitalOcean Spaces.
"""

import json
from pathlib import Path
from typing import Any

import boto3
from botocore.client import Config
from botocore.exceptions import ClientError

from helix_core.errors import StorageError
from helix_core.settings import HelixSettings

S3Client = Any
"""A boto3 S3 client. Untyped: helix-core has no dependency on boto3-stubs."""

_NOT_FOUND_ERROR_CODES = frozenset({"NoSuchKey", "404"})


def join_key(prefix: str, *parts: str) -> str:
    """Join S3 key parts with single slashes.

    Args:
        prefix: The leading part of the key, with or without a trailing slash.
        *parts: Additional key segments to append.

    Returns:
        The joined key, e.g. ``join_key("experiments/a/runs/b/", "model.onnx")`` ->
        ``"experiments/a/runs/b/model.onnx"``.
    """
    segments = [prefix, *parts]
    return "/".join(segment.strip("/") for segment in segments if segment.strip("/"))


class Storage:
    """Thin wrapper over a boto3 S3 client bound to one bucket."""

    def __init__(self, client: S3Client, bucket: str) -> None:
        """Initialize the storage client.

        Args:
            client: A boto3 S3 client.
            bucket: The name of the bucket this instance reads from and writes to.
        """
        self._client = client
        self._bucket = bucket

    @classmethod
    def from_settings(cls, settings: HelixSettings) -> "Storage":
        """Build a :class:`Storage` bound to the bucket from ``settings``.

        Uses path-style addressing, which MinIO/RustFS requires (virtual-hosted-style
        addressing would otherwise resolve to ``<bucket>.localhost``).

        Args:
            settings: The settings to read S3 credentials and endpoint from.

        Returns:
            A new :class:`Storage` instance.
        """
        client = boto3.client(
            "s3",
            endpoint_url=settings.s3_endpoint_url,
            region_name=settings.s3_region,
            aws_access_key_id=settings.s3_access_key.get_secret_value(),
            aws_secret_access_key=settings.s3_secret_key.get_secret_value(),
            config=Config(s3={"addressing_style": "path"}, signature_version="s3v4"),
        )
        return cls(client, settings.s3_bucket)

    def get_bytes(self, key: str) -> bytes:
        """Read an object's content as bytes.

        Args:
            key: The S3 key to read.

        Returns:
            The object's raw content.

        Raises:
            StorageError: If the object does not exist or can't be read.
        """
        try:
            response = self._client.get_object(Bucket=self._bucket, Key=key)
            return response["Body"].read()
        except ClientError as exc:
            raise self._to_storage_error(key, exc) from exc

    def put_bytes(self, key: str, data: bytes, content_type: str) -> None:
        """Write bytes to an object.

        Args:
            key: The S3 key to write.
            data: The raw content to write.
            content_type: The MIME type to store on the object.
        """
        self._client.put_object(
            Bucket=self._bucket, Key=key, Body=data, ContentType=content_type
        )

    def put_json(self, key: str, value: object) -> None:
        """Write a JSON-serializable value to an object.

        Encoded as UTF-8 with an indent of 2 and content type ``application/json``.

        Args:
            key: The S3 key to write.
            value: The value to serialize as JSON.
        """
        data = json.dumps(value, indent=2).encode("utf-8")
        self.put_bytes(key, data, "application/json")

    def get_json(self, key: str) -> Any:
        """Read and parse an object as JSON.

        Args:
            key: The S3 key to read.

        Returns:
            The parsed JSON value.

        Raises:
            StorageError: If the object does not exist or can't be read.
        """
        return json.loads(self.get_bytes(key))

    def exists(self, key: str) -> bool:
        """Check whether an object exists.

        Args:
            key: The S3 key to check.

        Returns:
            ``True`` if the object exists, ``False`` otherwise.
        """
        try:
            self._client.head_object(Bucket=self._bucket, Key=key)
        except ClientError as exc:
            if self._error_code(exc) in _NOT_FOUND_ERROR_CODES:
                return False
            raise
        return True

    def download_file(self, key: str, path: Path) -> None:
        """Download an object to a local file.

        Args:
            key: The S3 key to read.
            path: The local path to write the object's content to.

        Raises:
            StorageError: If the object does not exist or can't be read.
        """
        try:
            self._client.download_file(self._bucket, key, str(path))
        except ClientError as exc:
            raise self._to_storage_error(key, exc) from exc

    def upload_file(self, path: Path, key: str, content_type: str) -> None:
        """Upload a local file to an object.

        Args:
            path: The local file to upload.
            key: The S3 key to write.
            content_type: The MIME type to store on the object.
        """
        self._client.upload_file(
            str(path),
            self._bucket,
            key,
            ExtraArgs={"ContentType": content_type},
        )

    @staticmethod
    def _error_code(exc: ClientError) -> str:
        return exc.response.get("Error", {}).get("Code", "")

    @classmethod
    def _to_storage_error(cls, key: str, exc: ClientError) -> StorageError:
        if cls._error_code(exc) in _NOT_FOUND_ERROR_CODES:
            return StorageError(f"Object not found: {key}")
        return StorageError(f"Could not read object {key}: {exc}")
