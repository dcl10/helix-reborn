from pathlib import Path

from helix_core.contracts import build_schemas, export, find_repo_root, main


def test_build_schemas_has_the_messages_model_metadata_and_catalog() -> None:
    schemas = build_schemas()

    assert set(schemas) == {
        "training-job",
        "evaluation-job",
        "job-event",
        "pipeline-spec",
        "model-metadata",
        "pipeline-catalog",
    }


def test_export_writes_one_file_per_schema(tmp_path: Path) -> None:
    paths = export(tmp_path)

    assert len(paths) == 6
    for path in paths:
        assert path.exists()
        assert path.read_text(encoding="utf-8").endswith("\n")


def test_export_writes_model_metadata_at_the_top_level_of_contracts(
    tmp_path: Path,
) -> None:
    paths = export(tmp_path)

    model_metadata_path = tmp_path / "contracts" / "model-metadata.schema.json"
    assert model_metadata_path in paths
    assert not (
        tmp_path / "contracts" / "messages" / "model-metadata.schema.json"
    ).exists()


def test_find_repo_root_finds_the_real_repo_root() -> None:
    root = find_repo_root(Path(__file__).resolve())

    assert (root / "contracts").is_dir()
    assert (root / ".git").is_dir()


def test_main_check_is_up_to_date_against_the_committed_files() -> None:
    assert main(["check"]) == 0


def test_export_writes_the_pipeline_catalog_as_json(tmp_path: Path) -> None:
    paths = export(tmp_path)

    catalog_path = tmp_path / "contracts" / "pipeline-catalog.json"
    assert catalog_path in paths
    assert '"schema_version": 1' in catalog_path.read_text(encoding="utf-8")
