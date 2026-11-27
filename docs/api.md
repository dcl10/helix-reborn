# API

ASP.NET Core 10, controllers, EF Core on Postgres, Keycloak JWTs. See [architecture.md](architecture.md) for the
broader design (auth model, ownership rules, delivery) — this doc is about the API project itself.

## Project layout

```
api/
  Directory.Build.props          shared MSBuild settings (target framework, nullable, implicit usings)
  Directory.Packages.props       central package versions
  HelixReborn.slnx               solution
  Dockerfile                     build, bundle, migrate and api stages (see below)
  .config/dotnet-tools.json      pins dotnet-ef
  src/HelixReborn.Api/
    Program.cs                   composition root
    appsettings.json             defaults; appsettings.Development.json overrides for local dev
    Auth/                        Keycloak JWT setup, role claims, ICurrentUser
    Config/                      options classes (KeycloakOptions, HelixOptions, InvitationOptions, S3Options, EdaOptions,
                                 ContractsOptions, RabbitMqOptions)
    Controllers/                 one per resource, thin
    Filters/                     MVC filters (RejectOversizedUploadAttribute)
    Messaging/                   RabbitMQ: queue topology, job messages, publisher, jobs.events consumer
    Models/                      request/response types
    Data/                        AppDbContext, design-time factory, migrations
    Health/                      /health response writer
    Services/
      Experiments/               IExperimentService, ExperimentService, ExperimentNotFoundException
      Datasets/                  IDatasetService, DatasetService, CsvInspector and their exceptions
      Eda/                       IEdaService, EdaService; IEdaClient, EdaClient over the EDA service; their exceptions
      Pipelines/                 IPipelineCatalog (contracts/pipeline-catalog.json), PipelineSpec, PipelineSpecValidator,
                                 IPipelineService, PipelineService and their exceptions
      Runs/                      IRunService, RunService, RunEventHandler (training job events)
      Evaluations/               IEvaluationService, EvaluationService, EvaluationEventHandler (evaluation job events),
                                 EvaluationOptionsValidator and their exceptions
      Jobs/                      JobStatusFeed: polling a run or evaluation and streaming it as server-sent events
      Contracts/                 ContractFiles: finding files under contracts/
      Invitations/               IInvitationService, InvitationService, InvitationConflictException
      Keycloak/                  IKeycloakUserAdmin over the Keycloak admin API (Kiota client)
      Storage/                   IObjectStorage, S3ObjectStorage, StorageKeys
    Extensions/                  service registration extensions used by Program.cs
  tests/HelixReborn.Api.Tests/
    Fixtures/                    ApiFactory, test auth handler, test-only admin controller, InMemoryKeycloakUserAdmin,
                                 InMemoryObjectStorage, FakeEdaClient
```

## Configuration

Bound from `appsettings*.json`, user-secrets and environment variables (`__` for nesting, e.g.
`ConnectionStrings__DefaultConnection`). Validated at startup, so a bad value stops the app instead of failing on
first use.

- **`ConnectionStrings:DefaultConnection`** — Npgsql connection string for the `helix` database. Empty in
  `appsettings.json`; `appsettings.Development.json` points at the local `postgres-app` compose service.
- **`Keycloak`** (`KeycloakOptions`) — `AuthServerUrl`, `Realm` and `Resource` (the `helix-api` client ID) are
  required; the API won't start without them. `RequireHttpsMetadata` defaults to on everywhere except Development,
  and can be forced off for a private, plain-HTTP Keycloak address (e.g. `http://keycloak:8080` inside Compose).
  `Secret` is for the Keycloak admin API (invitations) and isn't needed for token validation.
- **`Invitations`** (`InvitationOptions`) — `AppUrl` (the web app's URL, must be a valid redirect URI of
  `helix-web`), `ClientId` (`helix-web`) and `ExpiryDays` (default 7), used to build the invited user's action email
  link.
- **`Helix`** (`HelixOptions`) — `Version`, reported by `/health` so the deploy workflow can confirm a rollout
  reached the expected image. Set from the image's `HELIX_VERSION` build arg; `"dev"` otherwise.
- **`S3`** (`S3Options`) — `ServiceUrl` (default `http://localhost:9000`, the local RustFS), `Region` (`us-east-1`),
  `Bucket` (`helix`), `ForcePathStyle` (`true`, which RustFS needs), `AccessKey` and `SecretKey`. The credentials are
  only in `appsettings.Development.json`; set them through the environment everywhere else.
- **`Eda`** (`EdaOptions`) — `BaseUrl` (default `http://localhost:8001`, EDA in Compose; use `http://localhost:8000`
  with `uv run helix-eda`, and `http://eda:8000` inside Compose, through `Eda__BaseUrl`), `Audience` (`helix-eda`,
  must match the Keycloak audience mapper and EDA's `HELIX_EDA_AUDIENCE`) and `TimeoutSeconds` (default 120, since
  t-SNE can be slow). Calls also need `Keycloak:Secret`; see [EDA](#eda).
- **`Contracts`** (`ContractsOptions`) — `Root`, the generated `contracts/` directory (default `contracts`). A relative
  path is looked for in the content root and each parent, then the app's base directory and each parent, so the repo's
  own `contracts/` is found from `dotnet run` and tests. The Docker image copies `contracts/` to `/app/contracts` and
  sets `Contracts__Root` to it. See [Pipelines](#pipelines).
- **`RabbitMq`** (`RabbitMqOptions`) — `Url`, an `amqp://` or `amqps://` URL with credentials. The default is the local
  Compose broker (`amqp://helix:dev_password@localhost:5672/`); set `RabbitMq__Url` everywhere else. See
  [Training runs](#training-runs).

## Auth

- Access tokens are validated against the `helix` realm's issuer, with audience `helix-api`. Claim names are kept as
  Keycloak sends them (`sub`, `email`, `preferred_username`, `realm_access`) — `MapInboundClaims` is off.
- `RealmRolesClaimsTransformation` copies `realm_access.roles` into standard role claims, so `IsInRole` and
  `RequireRole` see `helix-admin` / `helix-user` ([`Auth/Roles.cs`](../api/src/HelixReborn.Api/Auth/Roles.cs)).
- The fallback authorization policy requires a signed-in user on every endpoint that doesn't say otherwise, so a new
  endpoint can't be left open by accident. `/health` is the only anonymous endpoint. The `AdminOnly` policy requires
  `helix-admin`.
- Controllers read the caller through `ICurrentUser` (`UserId`, `Email`, `IsAdmin`) rather than the claims principal
  directly.

## Invitations

Helix is invite-only: nobody can create their own account. `helix-admin` users invite a colleague by email through
`InvitationsController`, under `api/invitations` and the `AdminOnly` policy. Errors come back as problem details with
a readable `detail`.

| Method | Route | Does |
| --- | --- | --- |
| `GET` | `/api/invitations` | Lists every invitation, newest first, with its current status. |
| `POST` | `/api/invitations` | Invites an email: creates the Keycloak user and sends the action email. `201` with the invitation. |
| `POST` | `/api/invitations/{id}/resend` | Resends a pending or expired invitation with a fresh link and expiry. |
| `POST` | `/api/invitations/{id}/revoke` | Revokes a pending or expired invitation by disabling the Keycloak user. |

`IInvitationService` (`Services/Invitations/`) owns the logic: it checks for an existing Keycloak user, creates one
through `IKeycloakUserAdmin`, sends Keycloak's `execute-actions-email` for `VERIFY_EMAIL` and `UPDATE_PASSWORD`, and
records the invitation (who was invited, by whom, when, and the link's expiry) in the `Invitations` table. Status
(`Pending` / `Accepted` / `Expired` / `Revoked`) is derived on read, not stored — see `Invitation.StatusAt`.

`IKeycloakUserAdmin` (`Services/Keycloak/`) wraps Keycloak's admin REST API via the Kiota client
(`KeycloakUserAdmin`), authenticating as the `helix-api` service account through
`Duende.AccessTokenManagement` client-credentials tokens. It requires **`Keycloak:Secret`** to be set — without it,
every call fails with a `KeycloakAdminException`, surfaced to callers as `502 Bad Gateway`. The app still starts with
no secret configured; only invitation calls fail. See [auth-setup.md](auth-setup.md#inviting-users) for the full
flow and `Keycloak:Secret`'s source.

Tests fake `IKeycloakUserAdmin` with `InMemoryKeycloakUserAdmin` (`tests/.../Fixtures/`), which `ApiFactory` registers
in place of `KeycloakUserAdmin`, so the test suite never calls a real Keycloak.

## Experiments and datasets

An experiment is the top-level thing a user works in. It holds one uploaded CSV dataset, and later its EDA
artifacts, pipelines, runs, evaluations and predictions. Every route needs a signed-in user. Users see only their own
experiments. `helix-admin` can read anyone's but can't change them: an admin's `PATCH`/`DELETE` on someone else's
experiment gets a `404`, the same as a missing one.

| Method | Route | Does |
| --- | --- | --- |
| `GET` | `/api/experiments` | The caller's experiments, newest first. Admins add `?all=true` to list every user's. |
| `POST` | `/api/experiments` | Creates one (`name` 1–100 characters, `description` up to 1000). `201` with `Location`. |
| `GET` | `/api/experiments/{id}` | One experiment, with its dataset if it has one. `404` if missing or not visible. |
| `PATCH` | `/api/experiments/{id}` | Updates `name` and/or `description`; a field left out (or `null`) is unchanged. Owner only. |
| `DELETE` | `/api/experiments/{id}` | Deletes the DB rows and every stored object under the experiment's prefix. `204`. Owner only. |
| `POST` | `/api/experiments/{id}/dataset` | Uploads the dataset (`multipart/form-data`, field `file`). Owner only. |
| `GET` | `/api/experiments/{id}/dataset` | The dataset's details and inferred columns, or `404`. |

Upload rules, checked by `CsvInspector` (CsvHelper, invariant culture):

- `.csv` extension and a content type of `text/csv`, `application/vnd.ms-excel` or `application/octet-stream`.
- A header row of 2–1000 unique, non-empty column names, and at least one data row.
- 50 MB at most, otherwise `413`. One dataset per experiment, otherwise `409`. Other failures are a `400`
  `ValidationProblemDetails` listing every problem under `file`.

Each column gets a `missingCount` (blank values) and an `inferredType`: `empty` if every value is blank, `numeric` if
every non-blank value parses as a `double`, `text` otherwise. The column list is stored as `jsonb` on the `Datasets`
row.

The upload is streamed once to a temp file, inspected, then uploaded from the temp file, so a 50 MB file is never held
in memory. `[RequestSizeLimit]` and `[RequestFormLimits]` set the 50 MB limit, but on their own a too-large body comes
back as a model-binding `400`. `RejectOversizedUploadAttribute` (`Filters/`) runs before model binding and returns
the `413` instead.

Objects follow the storage layout in [architecture.md](architecture.md#storage-layout-s3), built by `StorageKeys`
with lowercase hyphenated IDs:

```
experiments/{experimentId}/                                 StorageKeys.Experiment: everything for one experiment
experiments/{experimentId}/datasets/{datasetId}/{fileName}  StorageKeys.Dataset: the uploaded CSV
```

The client's file name is never trusted in a key: anything outside `[A-Za-z0-9._-]` becomes `_`, and it's cut to 100
characters. `IObjectStorage` (`Services/Storage/`) is the only way the API touches S3; `S3ObjectStorage` implements it
with `AmazonS3Client`, and `DeletePrefixAsync` deletes in batches of up to 1000 keys.

## Artifacts

An artifact is a record of one file an experiment owns: the dataset, EDA plots and tables, a run's model and
metadata, evaluation plots and tables, or predictions. `IArtifactService` (`Services/Artifacts/`) is the only way
other services register a file they've already uploaded to S3 — `DatasetService` calls `RecordAsync` right after it
uploads the dataset, and later issues (EDA, training, evaluation, prediction) do the same for their own output.
`ArtifactsController` only reads; artifacts are never created or deleted through the API itself. Same ownership
rules as experiments: owner, or `helix-admin` read-only.

| Method | Route | Does |
| --- | --- | --- |
| `GET` | `/api/experiments/{id}/artifacts?sourceType=&sourceId=` | The experiment's artifacts, oldest first. Both query params are optional and combine with AND. |
| `GET` | `/api/experiments/{id}/artifacts/{artifactId}/content` | Streams the file with its stored `Content-Type`. `?download=true` adds `Content-Disposition: attachment` with the storage key's last segment as the file name; otherwise `inline`. `404` if the artifact record or its S3 object is missing. |
| `GET` | `/api/experiments/{id}/archive` | `application/zip` streamed straight to the response, one entry per artifact, named by the storage key with the `experiments/{id}/` prefix stripped off. `Content-Disposition: attachment` with a sanitised `<experiment name>-<id8>.zip` file name. |

An `ArtifactResponse`:

```json
{ "id": "…", "kind": "evaluationPlot", "title": "ROC curve", "contentType": "image/png", "sizeBytes": 48211,
  "sourceType": "evaluation", "sourceId": "…", "createdAt": "…", "fileName": "roc_curve.png" }
```

`kind` is one of `dataset`, `edaPlot`, `edaTable`, `model`, `modelMetadata`, `split`, `cvMetrics`, `metrics`,
`evaluationPlot`, `evaluationTable`, `predictions` — the `ArtifactKind` enum, serialized camelCase. `sourceType` is
`"dataset"`, `"eda"`, `"run"`, `"evaluation"` or `"prediction"`, and `sourceId` the ID of that dataset, run,
evaluation or prediction; both are null for artifacts that aren't tied to one of those (there are none yet, since
this issue only records the dataset).

Both streaming endpoints write straight to `Response.Body`: `/content` copies the S3 object's stream across, and
`/archive` writes a `ZipArchive` (mode `Create`) over it, response buffering disabled, so neither holds a whole file
in memory. `ZipArchiveEntry` still does a synchronous `Write` on dispose even when opened with `OpenAsync`, so the
archive endpoint turns on `IHttpBodyControlFeature.AllowSynchronousIO` for itself rather than globally.

## EDA

The web app never talks to the EDA service ([python-services.md](python-services.md#eda-service)) directly. The API
exposes it under the experiment, checks ownership, calls EDA server-to-server and records every plot and table EDA
writes as an artifact (`sourceType` `"eda"`, no `sourceId`). Reads are for the owner or `helix-admin`; making plots
and tables is owner only, so an admin's `POST` on someone else's experiment gets a `404`.

| Method | Route | Does |
| --- | --- | --- |
| `GET` | `/api/experiments/{id}/eda/summary` | EDA's dataset summary JSON, passed through as-is (snake_case). |
| `POST` | `/api/experiments/{id}/eda/plots` | `{"type", "options", "plotOptions"}`: renders a plot. `201` with an `ArtifactResponse` (kind `edaPlot`), `Location` its `/content` URL. |
| `POST` | `/api/experiments/{id}/eda/tables/normality` | `{"columns": [...] \| null}`: Shapiro-Wilk and KS tests. `201` with `{"artifact": ArtifactResponse, "rows": [...]}` (kind `edaTable`; rows passed through from EDA, snake_case). |
| `GET` | `/api/experiments/{id}/eda/artifacts` | The experiment's EDA artifacts, oldest first. |

`type` is `targetDistribution`, `correlationHeatmap`, `pairplot`, `pca` or `tsne`, each EDA's `/plots/<kebab-case>`
route. `options` holds that plot's own fields in EDA's snake_case (e.g. `{"columns": ["a", "b"], "method":
"spearman"}`) and `plotOptions` EDA's `PlotOptions`; the API doesn't validate either, EDA does. The API always sets
`dataset_key` (the experiment's dataset) and `output_key`, overriding anything in `options`, so a request can't read
another experiment's data or write outside its own prefix.

For each plot or table, `EdaService` generates the artifact ID first, tells EDA to write to
`StorageKeys.Eda(experimentId, artifactId, "png" | "csv")` (`experiments/{e}/eda/{artifactId}.png`), and on success
records the artifact with that ID, EDA's title and the size EDA reports. Errors:

| Cause | Response |
| --- | --- |
| Experiment missing or not visible, or an admin writing | `404` |
| No dataset uploaded, or EDA can't find its file (EDA `404`) | `409` |
| EDA rejected the request (EDA `422`: unknown or non-numeric column, bad option) | `400`, EDA's message as `detail` |
| EDA unreachable, timed out, refused the token, or failed; or `Keycloak:Secret` unset | `502` |

**Service token.** `EdaClient` is a typed `HttpClient` (base address `Eda:BaseUrl`, timeout `Eda:TimeoutSeconds`)
with Duende.AccessTokenManagement's client-credentials handler. Its Duende client, `helix-eda`, signs in as the
`helix-api` service account (same client ID and `Keycloak:Secret` as the admin API client, separate token cache);
Keycloak's audience mapper puts `helix-eda` in those tokens' `aud`, which EDA requires. Tokens are cached until they
expire. Without `Keycloak:Secret`, `EdaClient` fails every call with a `502` before sending anything.

Tests replace `IEdaClient` with `FakeEdaClient`, which records calls and returns canned results, or throws for one
experiment's calls (`FailFor`). `EdaClientTests` build the real `AddEda` registration with Duende's token manager
stubbed to a fixed token and a stub `HttpMessageHandler`, so paths, bodies, the `Authorization` header and error
mapping are tested through the real handler chain.

## Pipelines

A pipeline is a saved, named `PipelineSpec` in an experiment: target, features, split, preprocessing steps, model and
optional hyperparameter search. The allowed steps come from `contracts/pipeline-catalog.json`, generated from
helix-core's registry (#22). The API serves the catalog and checks every saved pipeline against it; nothing in C# names
a step. Running pipelines is #25.

| Method | Route | Does |
| --- | --- | --- |
| `GET` | `/api/pipeline-catalog` | The catalog JSON exactly as in the file. Any signed-in user. |
| `GET` | `/api/experiments/{id}/pipelines` | The experiment's pipelines, by name. |
| `POST` | `/api/experiments/{id}/pipelines` | `{"name", "spec"}`: validates and saves one. `201` with `Location`. Owner only. |
| `GET` | `/api/experiments/{id}/pipelines/{pipelineId}` | One pipeline. |
| `PUT` | `/api/experiments/{id}/pipelines/{pipelineId}` | `{"name", "spec"}`: validates and replaces the name and spec. Owner only. |
| `DELETE` | `/api/experiments/{id}/pipelines/{pipelineId}` | `204`. Owner only. Runs keep their own copy of the spec (#25). |

A `PipelineResponse` is `{"id", "name", "spec", "createdAt", "updatedAt"}`. `spec` is the **snake_case** contract,
unlike the rest of the API: it's stored and sent to the training worker as is. Errors:

| Cause | Response |
| --- | --- |
| Experiment missing or not visible, pipeline not in it, or an admin writing | `404` |
| No dataset uploaded (the spec's columns are checked against it) | `409` |
| Name already used in the experiment | `409` |
| `name` missing or longer than 100 characters, or `spec` missing | `400` (model validation) |
| Invalid spec | `400` `ValidationProblemDetails` with every problem under `errors.spec`, e.g. `"steps[1].params.k: 0 should be at least 1"` |

**Catalog.** `PipelineCatalog` (singleton) reads `pipeline-catalog.json` from `Contracts:Root` and compiles each
step's `params_schema` with JsonSchema.Net, in a schema registry of its own. `PipelineCatalogStartupCheck`, a hosted
service, resolves it at startup, so a missing or invalid catalog stops the app instead of failing the first pipeline
request.

**Spec.** `PipelineSpec` and its records mirror helix-core's `PipelineSpec`. `PipelineSpecJson` reads and writes them
in snake_case with unknown properties rejected (helix-core's models are `extra="forbid"`), and fills in the defaults
for `split`, `steps` and `params`, so the stored spec states them. `PipelineSpecValidator` applies the same rules as
helix-core, plus checks against the dataset's columns; they're listed in
[pipelines.md](pipelines.md#validation-in-the-api). Parameter errors come from evaluating each step's params, and
each `param_grid` candidate on its own, against the step's JSON Schema. Errors from container keywords (`properties`,
`items`, `anyOf`) and the null branch of an optional parameter are dropped, so each problem is one line naming its
path.

**Storage.** `Pipelines` table: `Name` (up to 100 characters, unique per experiment), `SpecJson` (`jsonb`, the
normalised snake_case spec), timestamps, and a cascade delete with the experiment. Two concurrent saves with the same
name hit the unique index, and the loser gets the same `409`.

Tests: `PipelineCatalogTests` and `PipelineSpecValidatorTests` use the repo's real catalog, found the same way as
the app finds it. `PipelinesControllerTests` covers the endpoints, and `PipelineCatalogStartupTests` checks that the
app won't start without a catalog.

## Training runs

Running a pipeline creates a **run**. The API snapshots the pipeline's name and spec, stores a `Queued` run, and
publishes a `TrainingJob` to `training.jobs`. The training worker (`services/training`, see
[pipelines.md](pipelines.md#training)) publishes job events to `jobs.events`, and the API applies them to the run.
Reads are for the owner or `helix-admin`; only the owner can start a run.

| Method | Route | Does |
| --- | --- | --- |
| `POST` | `/api/experiments/{id}/pipelines/{pipelineId}/runs` | Re-validates the spec against the current dataset and catalog, stores a `Queued` run and publishes its job. `202` with the run and `Location`. |
| `GET` | `/api/experiments/{id}/runs` | The experiment's runs, newest first. |
| `GET` | `/api/experiments/{id}/runs/{runId}` | One run. |
| `GET` | `/api/experiments/{id}/runs/{runId}/events` | Server-sent events: `status` with the run (camelCase) now and whenever it changes, then `end` (`{"status"}`) once it's succeeded or failed, and the stream closes. |

A `RunResponse` is `{"id", "pipelineId", "pipelineName", "status", "progress", "message", "result", "error",
"spec", "createdAt", "startedAt", "finishedAt"}`:

- `status` is `queued`, `running`, `succeeded` or `failed`.
- `result` is the worker's `succeeded` result (`model_key`, `metadata_key`, `split_key`, `cv_metrics_key`,
  `cv_metrics`, `best_params`, `train_rows`, `test_rows`). Like `spec`, it's snake_case, as stored.
- `error` is `{"type", "message"}`.
- `pipelineId` becomes null if the pipeline is deleted. The run keeps its own snapshot.

Errors:

| Cause | Response |
| --- | --- |
| Experiment, pipeline or run missing or not visible, or an admin starting a run | `404` |
| No dataset | `409` |
| The spec no longer fits the dataset or catalog | `400` `ValidationProblemDetails`, `errors.spec` |
| The job couldn't be published (RabbitMQ down, not confirmed within 10 s) | `503`; the run is stored as `failed` with error type `QueueUnavailable` |

**Publishing.** `TrainingJob` values:

- `dataset_key`: the dataset's storage key.
- `output_prefix`: `StorageKeys.RunPrefix(e, r)` (`experiments/{e}/runs/{r}/`).
- `job_id`: a new ID, stored as the run's `JobId`.
- `pipeline`: the stored spec JSON, as is.

`RabbitMqJobPublisher` opens a channel per publish with publisher confirms, and publishes to the default exchange
with routing key `training.jobs`, persistent, `application/json`, `message_id` = `job_id`, and `mandatory`, so an
unroutable job fails instead of vanishing. `RabbitMqConnection` is the one long-lived connection, opened lazily and
again after it drops. It declares the topology on every new connection (`Messaging/Queues.cs`, the same arguments as
helix-core's).

**Consuming events.** `JobEventConsumer`, a `BackgroundService`, consumes `jobs.events`:

- Manual acks, prefetch 20.
- Each event goes to the `IJobEventHandler` whose `JobType` matches (`RunEventHandler` for `training`,
  `EvaluationEventHandler` for `evaluation`), in a fresh DI scope. An event with no handler is logged and acked.
- An unreadable event is logged and acked. A handler exception is nacked with requeue after a second.
- If RabbitMQ is down it retries with backoff (1 s doubling to 30 s), so the API starts and serves requests without
  it. RabbitMQ.Client's own automatic recovery is off, so a reconnect can't leave two consumers.

`RunEventHandler` (training) finds the run by `entity_id` and applies the event:

| Event | Effect |
| --- | --- |
| `started` | `Running`, `StartedAt` |
| `progress` | `Progress` and `Message`. Also `Running` and `StartedAt`, in case it arrived before `started` |
| `succeeded` | `Succeeded`, `Progress` 1, the result, `FinishedAt`. Records `model.onnx` (`model`), `metadata.json` (`modelMetadata`), `split.json` (`split`) and, if present, `cv_metrics.json` (`cvMetrics`) as artifacts with `sourceType` `"run"` and `sourceId` the run, using the sizes from `IObjectStorage.ListAsync` of the run's prefix |
| `failed` | `Failed`, the error's type and message, `FinishedAt` |

Events can arrive twice or out of order, so handling is idempotent:

- An event is ignored for an unknown run, for another job ID, or once the run is `Succeeded` or `Failed`.
- On `succeeded`, the files are recorded before the run turns terminal, skipping any already recorded. So a
  redelivered event finishes the job without duplicating artifacts.

**Following a run.** The SSE endpoint uses `TypedResults.ServerSentEvents`. `RunService.FollowAsync` checks access
first, so a 404 comes back as a problem rather than an empty stream. It then polls the run every second in a fresh DI
scope (never holding a `DbContext` for the whole stream) and yields it whenever its status, progress, message,
timestamps or error change. The polling and the `status`/`end` events are `Services/Jobs/JobStatusFeed`, shared with
evaluations.

Tests replace `IJobPublisher` with `FakeJobPublisher` (records jobs; `FailFor` makes one experiment's publishes throw)
and don't run `JobEventConsumer`. `RunEventHandlerTests` call the handler directly, and the SSE tests parse the stream
with `Fixtures/Sse.cs`.

## Evaluations

Evaluating a succeeded run's model creates an **evaluation**. The API validates the options, stores a `Queued`
evaluation with the options (defaults filled in) and publishes an `EvaluationJob` to `evaluation.jobs`. The
evaluation worker (`services/evaluation`, see [evaluation.md](evaluation.md)) reports back on `jobs.events` with
`job_type` `evaluation`, and `EvaluationEventHandler` applies the events. Reads are for the owner or `helix-admin`;
only the owner can start one. Evaluations are cascade deleted with their run and experiment.

| Method | Route | Does |
| --- | --- | --- |
| `POST` | `/api/experiments/{id}/runs/{runId}/evaluations` | Body `{"options": {…}}` (snake_case, optional; missing fields take the defaults). Validates, stores a `Queued` evaluation and publishes its job. `202` with the evaluation and `Location`. |
| `GET` | `/api/experiments/{id}/evaluations?runId=` | The experiment's evaluations, newest first; `runId` filters to one run's. |
| `GET` | `/api/experiments/{id}/evaluations/{evaluationId}` | One evaluation. |
| `GET` | `/api/experiments/{id}/evaluations/{evaluationId}/events` | Server-sent events, the same protocol as runs: `status` with the evaluation, then `end`. |

An `EvaluationResponse` is `{"id", "runId", "pipelineName", "status", "progress", "message", "options", "metrics",
"error", "createdAt", "startedAt", "finishedAt"}`:

- `status` is `queued`, `running`, `succeeded` or `failed`; `progress` and `message` are the worker's (its step names).
- `options` is what was sent to the worker, snake_case, with every default filled in.
- `metrics` is the `succeeded` result's `metrics`: the **test-set** metrics only. Train and test metrics are both in
  the `metrics.json` artifact.
- `pipelineName` is the run's, joined in on read.
- `error` is `{"type", "message"}`.

**Options.** Defaults, as helix-core's `EvaluationOptions`:

```json
{ "plots": true, "global_methods": ["permutation", "shap"], "permutation_repeats": 10,
  "local_methods": [], "local_max_samples": 50, "ensemble_methods": [], "num_features_to_plot": 10,
  "fuzzy": null, "plot_options": null }
```

`fuzzy`, when set, defaults to `{"number_fuzzy_features": 10, "granular_features": true, "number_clusters": 5,
"cluster_names": ["very low", "low", "medium", "high", "very high"], "number_rules": 5}`.

`EvaluationOptionsValidator` (a singleton) checks the options against the `EvaluationOptions` definition in
`contracts/messages/evaluation-job.schema.json` (JsonSchema.Net, compiled once), so ranges, enums and unknown fields
follow helix-core. It then applies the cross-field rules:

- `fuzzy` needs at least one local method and at least one ensemble method.
- `fuzzy.cluster_names` must have exactly `fuzzy.number_clusters` names (including the default five).
- `ensemble_methods` needs at least one global method (the worker's ensemble step would fail otherwise).

The options are then read into `EvaluationJobOptions` (`Messaging/JobMessages.cs`), whose property initialisers are
the defaults, and serialized back to snake_case for both the stored `OptionsJson` and the job. `plot_options` is passed
through as is.

Errors:

| Cause | Response |
| --- | --- |
| Experiment, run or evaluation missing or not visible, or an admin starting one | `404` |
| Invalid options | `400` `ValidationProblemDetails`, `errors.options` (`"field: problem"` messages) |
| The run isn't `succeeded`, or the experiment has no dataset | `409` |
| The job couldn't be published | `503`; the evaluation is stored as `failed` with error type `QueueUnavailable` |

**Publishing.** `EvaluationJob` values: `dataset_key` the experiment's dataset, `model_prefix`
`StorageKeys.RunPrefix(e, r)`, `output_prefix` `StorageKeys.EvaluationPrefix(e, v)`
(`experiments/{e}/evaluations/{v}/`), `job_id` a new ID stored as the evaluation's `JobId`, and the options.
`RabbitMqJobPublisher.PublishEvaluationJobAsync` publishes it exactly as training jobs are, with routing key
`evaluation.jobs`.

**Events.** `EvaluationEventHandler` is idempotent in the same ways as `RunEventHandler` (unknown evaluation, other job
ID or terminal state: ignored). On `succeeded` it stores `result.metrics` and records each `result.artifacts[]` entry
not already recorded, with `sourceType` `"evaluation"` and `sourceId` the evaluation:

| Entry `kind` | Artifact kind |
| --- | --- |
| `plot` | `evaluationPlot` |
| `table` | `evaluationTable` |
| `json` | `metrics` |

The title and key come from the entry, the content type from the key's extension (`.png` `image/png`, `.csv`
`text/csv`, `.json` `application/json`), and the size from `IObjectStorage.ListAsync` of the evaluation's prefix. An
entry whose file isn't in storage, or with no key or an unknown kind, is logged and skipped. The web app lists them
with `GET /api/experiments/{id}/artifacts?sourceType=evaluation&sourceId={evaluationId}`.

Tests: `EvaluationsControllerTests`, `EvaluationEventHandlerTests` (calling the handler directly, as for runs),
`EvaluationOptionsValidatorTests`, and `JobMessagesSchemaTests` validating the serialized `EvaluationJob` against the
contract schema. `Fixtures/EvaluationSetup.cs` builds a succeeded run and applies evaluation events.

## Running it

```bash
docker compose up -d postgres-app
cd api
dotnet run --project src/HelixReborn.Api
```

The http profile listens on `http://localhost:5080` (`Properties/launchSettings.json`) — 5000 clashes with macOS
AirPlay. `GET /api/me` is a quick way to check a token is accepted; `GET /health` needs none. For the EDA endpoints,
run the EDA service too (`cd services && uv run helix-eda`, port 8000) and start the API with
`Eda__BaseUrl=http://localhost:8000`.

## Migrations

The initial migration only creates EF Core's own history table. `AddInvitations` adds the `Invitations` table and
`AddExperiments` the `Experiments` and `Datasets` tables, `AddArtifacts` the `Artifacts` table, `AddPipelines` the
`Pipelines` table, and `AddRuns` the `Runs` table; further domain entities are added by later issues.

```bash
cd api
dotnet tool restore
dotnet ef migrations add <Name> --project src/HelixReborn.Api --output-dir Data/Migrations
```

Migrations never run at API startup. The `migrate` Docker stage builds an EF Core migrations bundle and applies it as
its own step (used by Compose from #39/#42), reading `ConnectionStrings__DefaultConnection` like the API does.

## Tests

xUnit with `WebApplicationFactory` (`ApiFactory`), on a real Postgres server rather than mocks. Each factory creates
its own database, `helix_test_<guid>`, migrates it, and drops it when the factory is disposed, so test classes stay
isolated and can run in parallel.

- `HELIX_TEST_POSTGRES` — Npgsql connection string for the server to create test databases on. Defaults to the
  `postgres-app` compose service (`Host=localhost;Port=5432;Database=postgres;Username=helix;Password=dev_password`).
- There's no Keycloak in tests: `TestAuthHandler` reads claims from an `X-Test-Claims` header instead of validating a
  JWT. `ApiFactory.CreateAnonymousClient()`, `CreateUserClient(...)` and `CreateAdminClient()` build clients with the
  right header set.
- `ApiFactory` replaces `IObjectStorage` with `InMemoryObjectStorage`, a dictionary keyed by object key, so tests
  never need RustFS, and `IEdaClient` with `FakeEdaClient`, so they never need the EDA service.
- `TestAdminController` (test assembly only) exercises the `AdminOnly` policy before the app has a real admin
  endpoint.

```bash
docker compose up -d postgres-app
cd api
dotnet build -warnaserror
dotnet test
```
