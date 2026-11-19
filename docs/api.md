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
    Config/                      options classes (KeycloakOptions, HelixOptions)
    Controllers/                 one per resource, thin
    Models/                      request/response types
    Data/                        AppDbContext, design-time factory, migrations
    Health/                      /health response writer
    Extensions/                  service registration extensions used by Program.cs
  tests/HelixReborn.Api.Tests/
    Fixtures/                    ApiFactory, test auth handler, test-only admin controller
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
  `Secret` is for the Keycloak admin API (#6) and isn't needed for token validation.
- **`Helix`** (`HelixOptions`) — `Version`, reported by `/health` so the deploy workflow can confirm a rollout
  reached the expected image. Set from the image's `HELIX_VERSION` build arg; `"dev"` otherwise.

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

## Running it

```bash
docker compose up -d postgres-app
cd api
dotnet run --project src/HelixReborn.Api
```

The http profile listens on `http://localhost:5080` (`Properties/launchSettings.json`) — 5000 clashes with macOS
AirPlay. `GET /api/me` is a quick way to check a token is accepted; `GET /health` needs none.

## Migrations

No domain entities yet — the initial migration only creates EF Core's own history table. Domain entities are added by
later issues.

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
- `TestAdminController` (test assembly only) exercises the `AdminOnly` policy before the app has a real admin
  endpoint.

```bash
docker compose up -d postgres-app
cd api
dotnet build -warnaserror
dotnet test
```
