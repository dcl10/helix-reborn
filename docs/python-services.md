# Python services

A single [uv](https://docs.astral.sh/uv/) workspace at `services/` holds the three Python services and the shared
`helix-core` library. See [architecture.md](architecture.md) for the broader design (what each service does, the
message contracts, the design rules) — this doc is about working in the workspace itself.

## Workspace layout

```
services/
  pyproject.toml                workspace root (virtual: tool.uv.package = false, not published)
  .python-version               3.12
  uv.lock                       one lockfile for the whole workspace
  .dockerignore
  packages/
    helix-core/                 shared library; never runs on its own
      pyproject.toml
      src/helix_core/
      tests/
  eda/                          FastAPI service (added by #16)
  training/                     aio-pika worker (added by #23)
  evaluation/                   aio-pika worker (added by #28)
```

Each of `eda`, `training` and `evaluation` is added to `[tool.uv.workspace].members` and `[tool.pytest.ini_options].testpaths`
by the issue that creates it.

### helix-core vs a service

Put code in **helix-core** when more than one of the three services needs it: settings, S3 storage, dataset loading,
the RabbitMQ message models, the worker base class, the pipeline step registry, ONNX export helpers, plotting
helpers, JWT validation, and contract export (see [architecture.md](architecture.md#components)). helix-core has no
runtime dependencies of its own yet; each service brings in what it needs (FastAPI, aio-pika, scikit-learn, etc.) and
depends on helix-core.

Put code in a **service** package when it's specific to that service: the FastAPI app and its routes (`eda`), the
training pipeline fitting logic (`training`), or evaluation metrics/plots/explainability (`evaluation`).

## Commands

Run these from `services/`.

```bash
# Install everything (all workspace packages, all dependency groups)
uv sync --all-packages --all-groups

# Add a runtime dependency to one package
uv add --package helix-core <dep>

# Lint and format
uv run ruff check .
uv run ruff format --check .      # uv run ruff format . to fix

# Test
uv run pytest
```

## Conventions

- **Packaging:** `uv` only — no pip, poetry or `requirements.txt`. Commit `uv.lock` whenever it changes.
- **Lint/format:** `ruff` only, configured in `services/pyproject.toml`. No black, isort or flake8 config anywhere.
- **Types:** every function and method signature has type annotations for all parameters and the return type.
- **Docstrings:** Google style (`Args:`, `Returns:`, `Raises:` where they apply) on every public module, class,
  function and method.
- **Settings:** read only through `helix_core.settings` (pydantic-settings, `HELIX_` prefix). Never read
  `os.environ` directly anywhere else.
- **Logging:** use `logging.getLogger(__name__)` and call `helix_core.logging.configure_logging()` once at startup.
  Never `print`; services log one JSON object per line to stdout.
- No pickles, no global mutable state, no writing to the local filesystem except temp files.

## Tests

pytest, in each package's `tests/` folder, run with `--import-mode=importlib` so packages can each have a `tests/`
folder without name clashes. Name tests `test_<function>_<behaviour>`. Use moto (S3) and fakes; tests never call
real external services.

Tests that need RabbitMQ, RustFS or Keycloak are marked `@pytest.mark.integration` and are skipped unless
`HELIX_INTEGRATION=1` is set in the environment.

## Settings and storage

`helix_core.settings.HelixSettings` (pydantic-settings) is the only way services read configuration; nothing else
reads `os.environ` directly. Call `helix_core.settings.get_settings()` to get the process-wide, cached instance.

| Variable | Default (local) | Used by |
|---|---|---|
| `HELIX_S3_ENDPOINT_URL` | `http://localhost:9000` | all |
| `HELIX_S3_REGION` | `us-east-1` | all |
| `HELIX_S3_ACCESS_KEY` / `HELIX_S3_SECRET_KEY` | `helix` / `dev_password` | all |
| `HELIX_S3_BUCKET` | `helix` | all |
| `HELIX_RABBITMQ_URL` | `amqp://helix:dev_password@localhost:5672/` | workers |
| `HELIX_WORKER_CPUS` | `1` | workers (`n_jobs` for scikit-learn/joblib) |
| `HELIX_HEALTH_PORT` | `8080` | workers |
| `HELIX_KEYCLOAK_ISSUER` | `http://localhost:8080/realms/helix` | EDA |
| `HELIX_KEYCLOAK_JWKS_URL` | issuer + `/protocol/openid-connect/certs` | EDA |
| `HELIX_EDA_AUDIENCE` | `helix-eda` | EDA |
| `HELIX_LOG_LEVEL` | `INFO` | all |
| `HELIX_VERSION` | `dev` | all (baked in at image build) |

`s3_access_key`, `s3_secret_key` and `rabbitmq_url` are `SecretStr`: use `.get_secret_value()` only where a value is
actually needed (building a client), never in logs or `repr`.

`helix_core.storage.Storage` wraps a boto3 S3 client bound to one bucket (`Storage.from_settings()` builds one from
`HelixSettings`, with path-style addressing, which RustFS needs). It offers `get_bytes`/`put_bytes`,
`get_json`/`put_json`, `exists`, and `download_file`/`upload_file`; a missing or unreadable object raises
`helix_core.errors.StorageError`. `helix_core.storage.join_key` joins S3 key segments with single slashes.

`helix_core.datasets` loads and validates tabular data per the data rules in
[architecture.md](architecture.md#data-rules-v1): `load_csv` reads a CSV from S3 (raising
`helix_core.errors.InvalidDataError` if it's empty or unreadable), `select_features_target` splits a dataframe into
features/target/IDs after checking the named columns exist, the features are numeric, and the target/ID columns
aren't also features, and `numeric_columns` lists a dataframe's numeric columns.
