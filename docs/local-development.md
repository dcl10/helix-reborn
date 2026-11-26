# Local development

## Prerequisites

- Docker, with Compose v2 (`docker compose`, not `docker-compose`)
- Node 22
- .NET 10 SDK
- [uv](https://docs.astral.sh/uv/)
- macOS only: `brew install libomp`, the OpenMP runtime XGBoost needs (helix-core's tests import it)

## Starting and stopping the dependencies

`docker-compose.yml` starts only the backing services (Postgres, Keycloak, RabbitMQ, object storage, MailHog). The
app's own services (web, api, eda, workers) are added under an `app` profile in #39; until then, run those yourself
(`npm run dev`, `dotnet run`, `uv run ...`).

```bash
docker compose up -d       # start the dependencies
docker compose ps          # check status - everything should be healthy or running
docker compose down        # stop them, keep the data
docker compose down -v     # stop them and delete all data (see Resetting data, below)
```

Keycloak takes 30-60 seconds to become reachable after it reports "started" in `docker compose ps`; give it a moment
before testing sign-in. It starts with no realm imported yet (#4 adds that).

## URLs, ports and credentials

| Service | URL | Port(s) | Credentials |
| --- | --- | --- | --- |
| Postgres (app) | `localhost:5432` | 5432 | `helix` / `dev_password`, database `helix` |
| Postgres (Keycloak) | `localhost:5433` | 5433 | `keycloak` / `keycloak_password`, database `keycloak` |
| Keycloak admin console | `http://localhost:8080/admin` | 8080 | `admin` / `admin` (from `KC_BOOTSTRAP_ADMIN_USERNAME`/`PASSWORD`, default) |
| RabbitMQ management UI | `http://localhost:15672` | 5672 (AMQP), 15672 (UI) | `helix` / `dev_password` |
| Object storage console (RustFS) | `http://localhost:9001` | 9000 (S3 API), 9001 (console) | `helix` / `dev_password` |
| MailHog inbox | `http://localhost:8025` | 1025 (SMTP), 8025 (UI) | none |

## Resetting data

```bash
docker compose down -v
```

This removes every named volume (`postgres-app-data`, `postgres-keycloak-data`, `rabbitmq-data`, `rustfs-data`), so
the next `docker compose up -d` starts from empty databases, an empty queue and an empty bucket. Keycloak also loses
any realm state (until #4, there's none to lose).

## Object storage: RustFS, not MinIO

MinIO stopped publishing new images to Docker Hub (and the `quay.io/minio/*` mirror also rejects anonymous pulls), so
local object storage uses [RustFS](https://github.com/rustfs/rustfs) instead, which is S3-API compatible. The
`rustfs-init` service creates the `helix` bucket on startup using the same image's bundled `rc` CLI (RustFS's
equivalent of MinIO's `mc`).
