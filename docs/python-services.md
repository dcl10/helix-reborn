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
  eda/                          FastAPI service (package helix-eda)
    pyproject.toml
    src/helix_eda/
    tests/
  training/                     aio-pika worker (added by #23)
  evaluation/                   aio-pika worker (added by #28)
```

Each of `training` and `evaluation` is added to `[tool.uv.workspace].members` and `[tool.pytest.ini_options].testpaths`
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

`helix_core.auth.JwtValidator` validates a Keycloak access token: RS256 signature against the realm's JWKS, issuer,
audience and expiry (30 seconds of leeway), raising `helix_core.auth.TokenValidationError` — never with the token in
the message — on any failure. It fetches signing keys with `jwt.PyJWKClient` (cached, 5-minute lifespan); tests inject
a fake JWKS client instead of serving real keys over HTTP.

## EDA service

`services/eda/` (package `helix-eda`, import `helix_eda`) is a synchronous FastAPI service for dataset summaries and
plots. Only the API calls it, server-to-server, with a Keycloak client-credentials token from the `helix-api` service
account (`aud: helix-eda`); see [architecture.md](architecture.md#auth). It never touches Postgres and never calls
Keycloak itself — it only validates tokens the API already obtained.

**Endpoints**

| Method & path | Auth | Body | Returns |
| --- | --- | --- | --- |
| `GET /health` | none | — | `{"status": "ok", "version": "…"}` |
| `POST /summary` | service token | `{"dataset_key": "…"}` | row/column counts and per-column summary statistics |
| `POST /plots/target-distribution` | service token | `{"dataset_key", "output_key", "target_column", "show_kde", "bins", "plot_options"}` | a PNG's key, content type, size and title |
| `POST /plots/correlation-heatmap` | service token | `{"dataset_key", "output_key", "columns", "method", "annotate", "plot_options"}` | a PNG's key, content type, size and title |
| `POST /plots/pairplot` | service token | `{"dataset_key", "output_key", "columns", "hue_column", "corner", "diag_kind", "plot_options"}` | a PNG's key, content type, size and title |
| `POST /plots/pca` | service token | `{"dataset_key", "output_key", "columns", "colour_column", "scaling", "plot_options"}` | a PNG's key, content type, size and title |
| `POST /plots/tsne` | service token | `{"dataset_key", "output_key", "columns", "colour_column", "scaling", "perplexity", "plot_options"}` | a PNG's key, content type, size and title |
| `POST /tables/normality` | service token | `{"dataset_key", "output_key", "columns"}` | a CSV's key, content type, size, title and the per-column test rows |

Every route but `/health` sits behind `helix_eda.dependencies.require_service_token`, which expects an
`Authorization: Bearer <token>` header and validates it with `helix_core.auth.JwtValidator` (built from
`HELIX_KEYCLOAK_ISSUER`, `HELIX_KEYCLOAK_JWKS_URL`/derived, and `HELIX_EDA_AUDIENCE`, see the settings table above). A
missing header, a malformed header, or a token that fails validation all get `401` with a `WWW-Authenticate: Bearer`
header. `helix_eda.dependencies.get_storage` builds the S3 client the same way the other services do; both it and the
validator dependency are `app.dependency_overrides` targets for tests.

`helix_eda.summary.summarise` computes per-column statistics with pandas: numeric columns get
mean/std/min/quartiles/max (rounded to 6 significant digits, `null` where a statistic can't be computed, e.g. an
all-missing column); non-numeric columns get the 10 most common values and their counts instead. The route loads the
dataset with `helix_core.datasets.load_csv`, which raises `InvalidDataError` (→ `422`) for an unreadable or empty CSV
and whose underlying `StorageError` (→ `404`) is raised when the key doesn't exist.

**Plots.** `helix_eda.plots` renders three plots with matplotlib/seaborn as pure `(df, request) -> (Figure, title)`
functions, ported from the original Helix's `plotting.py`; `helix_eda.routes_plots` loads the dataset, calls one of
them, uploads the PNG with `helix_core.plotting.save_figure` and returns its key, content type, size and title. Every
request takes `dataset_key`, `output_key` (must end `.png`, else `422`) and an optional `plot_options`
(`helix_core.messages.PlotOptions`); an unknown or non-numeric column, or too many columns, raises
`InvalidSpecError` (→ `422`). Route functions are plain `def`, not `async def`, so FastAPI runs the CPU-bound
rendering in its threadpool.

- `target-distribution`: a histogram with an optional KDE curve for a numeric `target_column` (`bins`, 5-200,
  `show_kde`), or a count bar chart for a non-numeric one.
- `correlation-heatmap`: a correlation matrix (`method`: `pearson`/`spearman`/`kendall`) over `columns` (default: all
  numeric, max 50), with a diverging colormap centred at 0 (`vmin`/`vmax` ±1), the upper triangle masked, and cell
  annotations only when there are at most 20 columns.
- `pairplot`: a seaborn pairplot over 2-10 numeric `columns`, with an optional `hue_column`, `corner` and `diag_kind`.
  Datasets over 2000 rows are sampled to 2000 (`random_state=42`), noted in the title.
- `pca`: a 2D scatter of the first two principal components (`sklearn.decomposition.PCA`,
  `n_components=2, random_state=42`) over `columns` (default: all numeric, min 2), scaled first per `scaling`
  (`standard`/`minmax`/`none`). Rows with a NaN in `columns` or `colour_column` are dropped before fitting. Axis
  labels include the explained variance, e.g. `"PC1 (43.2%)"`. `colour_column` colours points with a continuous
  colormap (numeric) or a categorical legend (otherwise).
- `tsne`: the same scatter, but with `sklearn.manifold.TSNE` (`n_components=2, random_state=42, init="pca"`, plus
  `perplexity`, 5-100, default 30). Datasets over 5000 rows (after dropping NaNs) are sampled to 5000
  (`random_state=42`), noted in the title; `perplexity` must be less than the row count after sampling, or the
  request gets `422`.

`helix_eda.stats.normality_table` runs Shapiro-Wilk and Kolmogorov-Smirnov (against a normal distribution fitted to
the column with `scipy.stats.norm.fit`) on each numeric column (default: all numeric), ported from the original
Helix's `statistical_tests.py`. Columns with fewer than 3 non-null values are skipped; Shapiro-Wilk runs on at most
5000 values (sampled with `random_state=42`). `helix_eda.routes_tables` loads the dataset, calls it, writes the
result as a CSV to S3 (`text/csv`, `output_key` must end `.csv`, else `422`) and returns its key, content type,
size, title and rows (`column`, `n`, `shapiro_statistic`, `shapiro_p`, `ks_statistic`, `ks_p`, and `normal_at_0_05`,
true when both p-values exceed 0.05).

Shared rendering helpers live in `helix_core.plotting` (used by the evaluation worker too): `use_headless_backend`
forces matplotlib's `Agg` backend (no display in a container); `apply_style` applies a `PlotOptions`' seaborn theme
and font-size rcParams; `finish_axes` sets a plot's title/labels/tick styling; `figure_to_png` renders a figure to PNG
bytes and closes it; `save_figure` does that and uploads the PNG to S3. `PlotOptions.style` is restricted to one of
`white`, `dark`, `whitegrid`, `darkgrid`, `ticks`.

**Running it locally**

```bash
cd services
uv run helix-eda            # or: uv run python -m helix_eda
```

Starts on `0.0.0.0:8000` (published on host port `8001`, see [architecture.md](architecture.md#local-ports)).
`docker compose up -d` must be running first so RustFS and Keycloak are available.

**Getting a token for manual testing.** Use the `helix-api` client's credentials against Keycloak (client secret from
the admin console, see [auth-setup.md](auth-setup.md#client-secrets)):

```bash
curl -s -X POST http://localhost:8080/realms/helix/protocol/openid-connect/token \
  -H 'Content-Type: application/x-www-form-urlencoded' \
  -d 'grant_type=client_credentials' \
  -d 'client_id=helix-api' \
  -d 'client_secret=<helix-api client secret>' \
  | jq -r .access_token
```

Then call the service:

```bash
curl -s http://localhost:8001/summary \
  -H "Authorization: Bearer <token>" \
  -H 'Content-Type: application/json' \
  -d '{"dataset_key": "experiments/.../datasets/.../data.csv"}'
```
