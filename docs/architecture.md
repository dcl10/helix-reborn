# Architecture

Helix Reborn is a hosted, invite-only, no-code app for building and evaluating machine learning models on tabular data.
It replaces the original Streamlit app ([Helix](https://github.com/Biomaterials-for-Medical-Devices-AI/Helix)). This
file is the source of truth for how the parts fit together and for every name shared between them. If code and this
file disagree, fix one of them in the same PR.

## Components

| Component | Folder | Tech | Role |
|---|---|---|---|
| Web | `web/` | Next.js 16, React 19, Auth.js (next-auth v5), Tailwind v4, shadcn/ui (`base-nova`) | The UI. Holds the session; calls the API through its own `/api/proxy/*` route. The browser never sees a Keycloak token. |
| API | `api/` | ASP.NET Core 10, EF Core + Npgsql, xUnit | Owns users' data (experiments, datasets, pipelines, runs, evaluations, predictions, artifacts). Publishes jobs to RabbitMQ, consumes job events, calls the EDA service, serves files, runs ONNX predictions. |
| EDA service | `services/eda/` | Python 3.12, FastAPI | Synchronous data summaries and exploratory plots. Called only by the API. |
| Training worker | `services/training/` | Python 3.12, scikit-learn, skl2onnx, aio-pika | Consumes `training.jobs`; builds and fits a scikit-learn `Pipeline`, exports it to ONNX. |
| Evaluation worker | `services/evaluation/` | Python 3.12, onnxruntime, shap, lime, scikit-fuzzy, aio-pika | Consumes `evaluation.jobs`; metrics, plots and feature importance computed on the exported ONNX model. |
| helix-core | `services/packages/helix-core/` | Python library | Shared code for the three Python services: settings, S3 storage, dataset loading, message models, worker base class, pipeline registry, ONNX export, plotting helpers, JWT validation, contract export. Never runs on its own. |
| Contracts | `contracts/` | Generated JSON | JSON Schemas and the pipeline catalog, generated from helix-core. Read by the API (and indirectly the web app). |
| Keycloak | `keycloak/` | Keycloak 26.6 | Sign-in. Realm `helix`, imported from `keycloak/realm-export.json`. |
| Postgres | (compose) | Postgres 16 | API database `helix`; Keycloak has its own database. |
| RabbitMQ | (compose) | RabbitMQ 4 with management plugin | Job queues between the API and the workers. |
| Object storage | (compose: RustFS; prod: DO Spaces) | S3 API | Datasets, plots, tables, models. |

```
browser ──> web ──/api/proxy──> API ──HTTP (service token)──> EDA service ──> S3
                                 │  ▲
                 training.jobs / │  │ jobs.events
               evaluation.jobs   ▼  │
                         training worker / evaluation worker ──> S3
                                 API ──> S3 (stream files, ONNX predictions)
```

## Design rules (hard constraints)

1. **The Python services never touch Postgres.** Everything they need arrives in a message or request, and everything
   they produce goes to S3 plus a job event (workers) or the HTTP response (EDA).
2. **Only the API is reachable from the web app.** EDA, RabbitMQ, RustFS/Spaces and the workers are internal.
3. **Every pipeline step must convert to ONNX.** A step is only added to the registry with a test that fits it, converts
   it and checks ONNX predictions match scikit-learn's.
4. **Evaluation and prediction use the ONNX model**, never a pickled scikit-learn object. No pickles are stored.
5. **helix-core is the source of truth for contracts.** Message models and the pipeline catalog are defined in Python
   and exported to `contracts/`; the API's C# types are tested against those files.
6. **Workers are stateless and Kubernetes-ready:** config from env vars only, data via S3 only, one job at a time
   (prefetch 1), graceful shutdown on SIGTERM, a health endpoint, JSON logs on stdout.
7. **Images contain no secrets.** Images are public on GHCR; all configuration comes from the environment.

## Auth

- Keycloak realm `helix`. Registration is off: users are invited by an admin (the API creates the user through the
  Keycloak admin API and Keycloak emails a "set your password" link).
- Realm roles: `helix-admin` (manages invitations, may read every user's data) and `helix-user` (default role).
- Clients:
  - `helix-web`: confidential, standard (authorization code) flow, used by Auth.js. Its access tokens carry `aud`
    `helix-api` (audience mapper) and a `realm roles` mapper.
  - `helix-api`: confidential, service account enabled (client credentials). The API validates user tokens with
    audience `helix-api`. Its service account has `realm-management` roles `manage-users`, `view-users` and
    `query-users` (invitations), and its tokens carry `aud` `helix-eda` (audience mapper) so it can call EDA.
  - `helix-eda`: confidential, no flows enabled. Exists only as an audience.
- The EDA service validates bearer JWTs against the realm JWKS: signature, issuer, expiry and audience `helix-eda`.
- Ownership: every experiment has an `OwnerSub` (the Keycloak `sub`). Users see only their own experiments and
  everything under them; `helix-admin` can read all. All API endpoints require a signed-in user except `/health`.

## Storage layout (S3)

One bucket (`HELIX_S3_BUCKET`, default `helix`). Keys:

```
experiments/{experimentId}/datasets/{datasetId}/{fileName}          uploaded CSV
experiments/{experimentId}/eda/{artifactId}.png                     EDA plot
experiments/{experimentId}/eda/{artifactId}.csv                     EDA table
experiments/{experimentId}/runs/{runId}/model.onnx                  trained pipeline
experiments/{experimentId}/runs/{runId}/metadata.json               see contracts/model-metadata.schema.json
experiments/{experimentId}/runs/{runId}/split.json                  {"train_index": [...], "test_index": [...]}
experiments/{experimentId}/runs/{runId}/cv_metrics.json             only when cross-validation ran
experiments/{experimentId}/evaluations/{evaluationId}/metrics.json
experiments/{experimentId}/evaluations/{evaluationId}/plots/{name}.png
experiments/{experimentId}/evaluations/{evaluationId}/tables/{name}.csv
experiments/{experimentId}/predictions/{predictionId}/predictions.csv
```

IDs are lowercase UUIDs with hyphens. Row indices in `split.json` are 0-based positions in the uploaded CSV
(after dropping nothing).

## Data rules (v1)

- Uploads are CSV only (UTF-8, comma-separated, header row), max 50 MB.
- One dataset per experiment.
- A pipeline names a target column, the feature columns (all must be numeric; missing values allowed and handled by
  an imputer step) and an optional ID column (excluded from features, kept in prediction output).
- Classification targets may be strings or numbers. Training label-encodes them to `0..k-1`; `metadata.json` lists the
  original class labels in index order.

## Messaging (RabbitMQ)

All queues are durable and declared idempotently by both publisher and consumer with exactly these arguments.

| Queue | Publisher | Consumer | Arguments |
|---|---|---|---|
| `training.jobs` | API | training worker | `x-dead-letter-exchange: helix.dlx`, `x-dead-letter-routing-key: training.jobs.dead` |
| `evaluation.jobs` | API | evaluation worker | `x-dead-letter-exchange: helix.dlx`, `x-dead-letter-routing-key: evaluation.jobs.dead` |
| `jobs.events` | workers | API | none |
| `training.jobs.dead`, `evaluation.jobs.dead` | (DLX) | nobody (inspect manually) | bound to direct exchange `helix.dlx` with routing key = queue name |

- Jobs and events are published to the default exchange (routing key = queue name), persistent
  (`delivery_mode=2`), `content_type=application/json`, with `message_id` = `job_id`.
- Workers use prefetch 1. On success or on a handled failure (bad data, invalid spec) the worker publishes a `failed`
  or `succeeded` event and **acks**. On an unexpected exception it publishes `failed` and **rejects without requeue**
  (message goes to the dead-letter queue). On SIGTERM during a job it stops the job and **nacks with requeue**.
- RabbitMQ's `consumer_timeout` is raised to 6 hours (`rabbitmq.conf`) because training can run long.
- Message shapes are defined by pydantic models in `helix_core.messages` and exported to
  `contracts/messages/*.schema.json`. Every message has `"schema_version": 1`. Field names are snake_case on the wire.

### TrainingJob

```json
{
  "schema_version": 1,
  "job_id": "1b4e28ba-2fa1-11d2-883f-0016d3cca427",
  "run_id": "…", "experiment_id": "…",
  "dataset_key": "experiments/{e}/datasets/{d}/data.csv",
  "output_prefix": "experiments/{e}/runs/{r}/",
  "pipeline": { "…": "PipelineSpec, below" }
}
```

### PipelineSpec

```json
{
  "problem_type": "classification",
  "target_column": "label",
  "feature_columns": ["a", "b", "c"],
  "id_column": null,
  "split": { "test_size": 0.2, "random_seed": 42, "cv_folds": 5 },
  "steps": [
    { "step": "simple_imputer", "params": { "strategy": "median" } },
    { "step": "standard_scaler", "params": {} }
  ],
  "model": { "step": "random_forest_classifier", "params": { "n_estimators": 200 } },
  "search": { "method": "grid", "cv_folds": 5, "n_iter": null, "param_grid": null }
}
```

- `problem_type`: `classification` | `regression`.
- `split.test_size`: 0.05–0.5. `split.cv_folds`: null or 2–10 (cross-validation metrics on the training portion).
- `steps`: zero or more steps whose catalog `kind` is `preprocessor` or `feature_selector`, applied in order.
- `model`: exactly one step whose catalog `kind` is `model`.
- `search`: null, or hyperparameter search on the training portion. `method`: `grid` | `random`; `n_iter` required
  for `random`; `param_grid` null means "use the catalog's default `search_grid` for the model".
- Every `step` id must exist in `contracts/pipeline-catalog.json`, support the `problem_type`, and its `params` must
  validate against that step's `params_schema`.

### EvaluationJob

```json
{
  "schema_version": 1,
  "job_id": "…", "evaluation_id": "…", "run_id": "…", "experiment_id": "…",
  "dataset_key": "experiments/{e}/datasets/{d}/data.csv",
  "model_prefix": "experiments/{e}/runs/{r}/",
  "output_prefix": "experiments/{e}/evaluations/{v}/",
  "options": {
    "plots": true,
    "global_methods": ["permutation", "shap"],
    "permutation_repeats": 10,
    "local_methods": ["lime", "shap"],
    "local_max_samples": 50,
    "ensemble_methods": ["mean", "majority_vote"],
    "num_features_to_plot": 10,
    "fuzzy": null,
    "plot_options": null
  }
}
```

`fuzzy`, when not null: `{"number_fuzzy_features": 10, "granular_features": true, "number_clusters": 5,
"cluster_names": ["very low", "low", "medium", "high", "very high"], "number_rules": 5}`. Fuzzy needs at least one
local method and at least one ensemble method.

### JobEvent (workers → `jobs.events`)

```json
{
  "schema_version": 1,
  "job_id": "…",
  "job_type": "training",
  "entity_id": "…run_id or evaluation_id…",
  "status": "progress",
  "progress": 0.4,
  "message": "Cross-validating (fold 2 of 5)",
  "result": null,
  "error": null,
  "timestamp": "2026-11-18T12:00:00Z"
}
```

- `status`: `started` | `progress` | `succeeded` | `failed`. `progress` is 0.0–1.0 or null.
- `error` (only on `failed`): `{"type": "InvalidDataError", "message": "…"}`.
- `result` (only on `succeeded`):
  - training: `{"model_key", "metadata_key", "split_key", "cv_metrics_key" (or null), "cv_metrics"
    ({"metric": {"mean", "std"}} or null), "best_params" (or null), "train_rows", "test_rows"}`
  - evaluation: `{"metrics": {"name": number}, "artifacts": [{"key", "kind": "plot" | "table" | "json", "title"}]}`

## Model artifacts

- `model.onnx`: the whole fitted pipeline (preprocessing + model). One input named `input`, type `float32`, shape
  `[None, n_features]`, columns in `metadata.json` `feature_columns` order. Classifiers are converted with
  `zipmap=False`, so outputs are `label` (int64 class index) and `probabilities` (float32 `[None, k]`). Regressors
  output `variable` (float32 `[None, 1]`). Actual output names are read from the session and stored in metadata.
- `metadata.json`: validated by `contracts/model-metadata.schema.json`: `schema_version`, `run_id`, `problem_type`,
  `target_column`, `feature_columns`, `id_column`, `classes` (null for regression), `onnx_input_name`,
  `onnx_outputs` (`{"label": "...", "probabilities": "..." | null}` or `{"value": "..."}`), `pipeline` (the
  PipelineSpec), `library_versions`, `helix_version`, `created_at`.

## Configuration

Python services use `pydantic-settings` with prefix `HELIX_`:

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

The API uses ASP.NET configuration sections (env vars use `__`, e.g. `S3__Bucket`): `ConnectionStrings:DefaultConnection`,
`Keycloak` (`AuthServerUrl`, `Realm` = `helix`, `Resource` = `helix-api`, `Secret`, `RequireHttpsMetadata`),
`Invitations`, `S3` (`ServiceUrl`, `Region`, `AccessKey`, `SecretKey`, `Bucket`, `ForcePathStyle`), `RabbitMq`
(`Url`), `Eda` (`BaseUrl`, `Audience` = `helix-eda`), `Helix` (`Version`).

## Local ports

| Service | Host port |
|---|---|
| web | 3000 |
| API | 5080 (container 8080) |
| Keycloak | 8080 |
| EDA | 8001 (container 8000) |
| Postgres (app / Keycloak) | 5432 / 5433 |
| RabbitMQ (AMQP / management UI) | 5672 / 15672 |
| RustFS (S3 / console) | 9000 / 9001 |
| MailHog (SMTP / UI) | 1025 / 8025 |

## Delivery

- **CI** (`.github/workflows/ci.yml`) runs on PRs to `main`; the aggregate `ci-ok` job is the required check.
- **Images** (`images.yml`) are built on every push to `main` and pushed to public GHCR:
  `ghcr.io/dcl10/helix-reborn-{web,api,migrate,eda,training,evaluation}`, tagged `sha-<7 chars>` and `main`, with
  `HELIX_VERSION` and the OCI label `org.opencontainers.image.revision` set to the commit SHA.
- **Deploy** (`deploy.yml`, manual) re-tags one SHA's images as `prod`. A systemd timer on the droplet pulls `:prod`,
  checks out the matching commit's deploy files, and runs `docker compose up -d`. Nothing connects into the droplet
  from GitHub.
- **Infra** (`infra.yml`, manual) runs Terraform with state in a Spaces bucket.
- **Hosting:** v1 is Docker Compose on one droplet. The planned next step is DigitalOcean Kubernetes with KEDA scaling
  workers on queue length; nothing in the services may assume a single host.
