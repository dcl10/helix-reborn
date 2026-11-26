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
                                 ContractsOptions)
    Controllers/                 one per resource, thin
    Filters/                     MVC filters (RejectOversizedUploadAttribute)
    Models/                      request/response types
    Data/                        AppDbContext, design-time factory, migrations
    Health/                      /health response writer
    Services/
      Experiments/               IExperimentService, ExperimentService, ExperimentNotFoundException
      Datasets/                  IDatasetService, DatasetService, CsvInspector and their exceptions
      Eda/                       IEdaService, EdaService; IEdaClient, EdaClient over the EDA service; their exceptions
      Pipelines/                 IPipelineCatalog (contracts/pipeline-catalog.json), PipelineSpec, PipelineSpecValidator,
                                 IPipelineService, PipelineService and their exceptions
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
`AddExperiments` the `Experiments` and `Datasets` tables, `AddArtifacts` the `Artifacts` table, and `AddPipelines` the
`Pipelines` table; further domain entities are added by later issues.

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
