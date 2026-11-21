from pathlib import Path

import pytest

from helix_core.errors import StorageError
from helix_core.settings import HelixSettings
from helix_core.storage import Storage, join_key


def test_put_bytes_get_bytes_round_trips(s3_storage: Storage) -> None:
    s3_storage.put_bytes("a/b.bin", b"hello", "application/octet-stream")

    assert s3_storage.get_bytes("a/b.bin") == b"hello"


def test_put_json_get_json_round_trips(s3_storage: Storage) -> None:
    value = {"a": 1, "b": [1, 2, 3]}

    s3_storage.put_json("a/b.json", value)

    assert s3_storage.get_json("a/b.json") == value


def test_exists_is_true_for_an_existing_key(s3_storage: Storage) -> None:
    s3_storage.put_bytes("a/b.bin", b"hello", "application/octet-stream")

    assert s3_storage.exists("a/b.bin") is True


def test_exists_is_false_for_a_missing_key(s3_storage: Storage) -> None:
    assert s3_storage.exists("does/not/exist") is False


def test_get_bytes_raises_storage_error_for_a_missing_key(s3_storage: Storage) -> None:
    with pytest.raises(StorageError):
        s3_storage.get_bytes("does/not/exist")


def test_upload_file_download_file_round_trips(
    s3_storage: Storage, tmp_path: Path
) -> None:
    source = tmp_path / "source.txt"
    source.write_text("hello")

    s3_storage.upload_file(source, "a/b.txt", "text/plain")

    destination = tmp_path / "destination.txt"
    s3_storage.download_file("a/b.txt", destination)
    assert destination.read_text() == "hello"


def test_download_file_raises_storage_error_for_a_missing_key(
    s3_storage: Storage, tmp_path: Path
) -> None:
    with pytest.raises(StorageError):
        s3_storage.download_file("does/not/exist", tmp_path / "out.txt")


def test_join_key_normalises_slashes() -> None:
    joined = join_key("experiments/a/runs/b/", "model.onnx")
    assert joined == "experiments/a/runs/b/model.onnx"
    assert join_key("a", "b", "c") == "a/b/c"
    assert join_key("/a/", "/b/") == "a/b"


def test_from_settings_uses_path_style_addressing() -> None:
    settings = HelixSettings(
        s3_endpoint_url="http://localhost:9000",
        s3_access_key="helix",
        s3_secret_key="dev_password",
        s3_bucket="helix",
    )

    storage = Storage.from_settings(settings)

    assert storage._client.meta.config.s3["addressing_style"] == "path"
