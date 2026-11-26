"""Generate the contract files shared with the API and the web app.

Everything under ``contracts/`` is generated, not hand-written: the JSON Schema form of
the pydantic models in :mod:`helix_core.messages` and :mod:`helix_core.model_metadata`,
and the pipeline catalog from :func:`helix_core.pipelines.catalog`. Python is the
source of truth.
"""

import argparse
import json
import sys
from collections.abc import Callable
from pathlib import Path
from typing import Any

from helix_core.messages import EvaluationJob, JobEvent, PipelineSpec, TrainingJob
from helix_core.model_metadata import ModelMetadata
from helix_core.pipelines import catalog

_MESSAGE_SCHEMA_BUILDERS: dict[str, Callable[[], dict[str, Any]]] = {
    "training-job": TrainingJob.model_json_schema,
    "evaluation-job": EvaluationJob.model_json_schema,
    "job-event": JobEvent.model_json_schema,
    "pipeline-spec": PipelineSpec.model_json_schema,
}

_TOP_LEVEL_SCHEMA_BUILDERS: dict[str, Callable[[], dict[str, Any]]] = {
    "model-metadata": ModelMetadata.model_json_schema,
}

_DOCUMENT_BUILDERS: dict[str, Callable[[], dict[str, Any]]] = {
    "pipeline-catalog": catalog,
}
"""Generated documents that aren't JSON Schemas: ``contracts/<name>.json``."""


def build_schemas() -> dict[str, dict[str, Any]]:
    """Build every contract: the message and top-level schemas, and the catalog.

    Returns:
        A mapping of contract name (e.g. ``"training-job"``, ``"model-metadata"``,
        ``"pipeline-catalog"``) to its content.
    """
    builders = (
        _MESSAGE_SCHEMA_BUILDERS | _TOP_LEVEL_SCHEMA_BUILDERS | _DOCUMENT_BUILDERS
    )
    return {name: builder() for name, builder in builders.items()}


def _schema_path(root: Path, name: str) -> Path:
    if name in _DOCUMENT_BUILDERS:
        return root / "contracts" / f"{name}.json"
    if name in _TOP_LEVEL_SCHEMA_BUILDERS:
        return root / "contracts" / f"{name}.schema.json"
    return root / "contracts" / "messages" / f"{name}.schema.json"


def _render(schema: dict[str, Any]) -> str:
    return json.dumps(schema, indent=2, sort_keys=True) + "\n"


def export(root: Path) -> list[Path]:
    """Write every contract under ``<root>/contracts/``.

    Message schemas go to ``messages/<name>.schema.json``, top-level schemas to
    ``<name>.schema.json`` and other documents (the pipeline catalog) to
    ``<name>.json``.

    Args:
        root: The repository root the ``contracts/`` directory lives under.

    Returns:
        The paths written, one per contract.
    """
    paths = []
    for name, schema in build_schemas().items():
        path = _schema_path(root, name)
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(_render(schema), encoding="utf-8")
        paths.append(path)
    return paths


def _check(root: Path) -> bool:
    for name, schema in build_schemas().items():
        path = _schema_path(root, name)
        if not path.exists() or path.read_text(encoding="utf-8") != _render(schema):
            return False
    return True


def find_repo_root(start: Path) -> Path:
    """Find the repository root by walking up from ``start``.

    Args:
        start: The path to start searching from (typically this file's location).

    Returns:
        The first ancestor directory (inclusive) containing a ``contracts/`` or
        ``.git`` directory.

    Raises:
        FileNotFoundError: If no such ancestor exists.
    """
    for candidate in (start, *start.parents):
        if (candidate / "contracts").is_dir() or (candidate / ".git").is_dir():
            return candidate
    raise FileNotFoundError(f"Could not find a repository root above {start}")


def main(argv: list[str] | None = None) -> int:
    """Run the contracts CLI.

    Args:
        argv: Command-line arguments, excluding the program name. Defaults to
            ``sys.argv[1:]``.

    Returns:
        The process exit code: ``0`` on success, ``1`` if `check` finds a mismatch.
    """
    parser = argparse.ArgumentParser(prog="python -m helix_core.contracts")
    subparsers = parser.add_subparsers(dest="command", required=True)

    export_parser = subparsers.add_parser(
        "export", help="Write the generated schema files."
    )
    export_parser.add_argument("--root", type=Path, default=None)

    check_parser = subparsers.add_parser(
        "check", help="Exit 1 if the schema files don't match the models."
    )
    check_parser.add_argument("--root", type=Path, default=None)

    args = parser.parse_args(argv)
    root = args.root or find_repo_root(Path(__file__).resolve())

    if args.command == "export":
        for path in export(root):
            print(f"Wrote {path}")
        return 0

    if _check(root):
        print("Contracts are up to date.")
        return 0
    print("Contracts are out of date. Run `python -m helix_core.contracts export`.")
    return 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
