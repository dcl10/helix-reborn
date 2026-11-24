# Helix Reborn: Agent Instructions

Helix Reborn is an invite-only, no-code web app for building, training and evaluating machine learning models on
tabular data. It has a Next.js web app (`web/`), an ASP.NET Core 10 API (`api/`), Python services (`services/`:
EDA, training and evaluation, plus the shared `helix-core` library), Keycloak (`keycloak/`), and Terraform for
DigitalOcean (`infra/`). How it works is documented in [docs/](docs/README.md); this file holds the rules only.

**Read [docs/architecture.md](docs/architecture.md) before changing anything.** Its design rules, names (queues,
S3 keys, env vars, Keycloak clients, ports) and message shapes are hard constraints. If a change needs one of them to
change, update architecture.md in the same PR.

---

## Development workflow

- Work on a branch named `feature/<name>` or `fix/<name>`, branched from an up-to-date `main`.
- Commit in small, focused commits grouped by type: app code, then tests (separate commits), then docs, with
  `CLAUDE.md`/`AGENTS.md` changes last. Commit messages use the conventional prefix style:
  `feat:`, `fix:`, `test:`, `docs:`, `chore:`, `refactor:`.
- Open PRs against `main` with a body following `.github/pull_request_template.md`.
- **Running unattended (the Zen bot):** don't create branches, push, open PRs or call `gh`. The controller does that.
  Just commit on the branch you're given. Your final message becomes the PR body: use the PR template's headings.
- Follow the issue's scope. If something nearby looks wrong, mention it in the PR notes instead of fixing it.
- Never commit secrets or `.env` files. Local-only default passwords already in `docker-compose.yml` are fine.
- Files under `.github/workflows/` are maintained by hand; don't edit them.

---

## Build & test

Run the checks for every part you touched before committing.

```bash
# Local dependencies (Postgres, Keycloak, RabbitMQ, RustFS, MailHog)
docker compose up -d

# Web
cd web && npm ci && npm run lint -- --max-warnings=0 && npm test && npm run build

# API (needs the postgres-app service running)
cd api && dotnet build && dotnet test

# Python services (uv workspace at services/)
cd services && uv sync --all-packages --all-groups
cd services && uv run ruff check . && uv run ruff format --check . && uv run pytest

# Contracts are generated from helix-core; regenerate after changing message models or the pipeline registry
cd services && uv run python -m helix_core.contracts export

# Terraform
terraform fmt -check -recursive infra
```

If a command doesn't exist yet because that part of the repo hasn't been built, skip it and say so in the PR notes.

---

## Code style

Follow `.editorconfig` for formatting basics.

### Python (`services/`)

- **Packaging:** `uv` only (no pip, poetry or requirements.txt). Add dependencies with `uv add --package <name> <dep>`
  from `services/`, and commit `uv.lock`.
- **Lint and format:** `ruff` only (no black, isort or flake8). Config lives in `services/pyproject.toml`.
  `ruff check` and `ruff format --check` must pass.
- **Types:** every function and method signature has type annotations for all parameters and the return type.
- **Docstrings:** Google style for all public modules, classes, functions and methods. Include `Args:`, `Returns:`
  and `Raises:` where they apply.
- **Settings:** come from `helix_core.settings` (pydantic-settings, `HELIX_` prefix). Never read `os.environ` directly
  elsewhere.
- **Logging:** use `logging.getLogger(__name__)`; never `print`. Services log JSON to stdout via `helix_core.logging`.
- No pickles, no global mutable state, no writing to the local filesystem except temp files.

### C# (`api/`)

- Controllers (`[ApiController]`, one per resource in `Controllers/`), not minimal APIs. Keep them thin; logic lives
  in services behind interfaces so tests can fake them. Request/response types go in `Models/`.
- Options classes in `Config/`, bound and validated at startup.
- All public types and members have XML doc comments (`<summary>`, `<param>`, `<returns>`).

### TypeScript (`web/`)

- Next.js 16 changes often. Before writing Next.js code, read the matching guide in
  `web/node_modules/next/dist/docs/`, not what you remember.
- Function components with hooks. Tailwind v4 + shadcn/ui, style `base-nova` (Base UI, not Radix: compose with
  the `render` prop, not `asChild`). Add primitives with `npx shadcn@latest add <component>`. Never hand-edit
  `components/ui/*`. Forms use shadcn's `field` component with react-hook-form + zod. Icons from `lucide-react` only.
- The browser never holds a Keycloak token. Read the user with `getCurrentUser()` and call the API through
  `/api/proxy/*`.
- All exported functions, components and hooks have JSDoc comments.

---

## Testing

- **Python:** pytest, in each package's `tests/` folder. Name tests `test_<function>_<behaviour>`. Use moto (S3) and
  fakes; tests never call real external services. Tests that need RabbitMQ, RustFS or Keycloak are marked
  `@pytest.mark.integration` and skipped unless `HELIX_INTEGRATION=1`.
- **API:** xUnit in `api/tests/HelixReborn.Api.Tests/`. Name tests `MethodName_ExpectedBehaviour_Condition`. Use
  `WebApplicationFactory` integration tests on a real Postgres (from `docker compose`), with a test auth scheme
  instead of Keycloak and fakes for S3, RabbitMQ, EDA and the Keycloak admin API.
- **Web:** Vitest + React Testing Library. Colocate `Component.test.tsx` files. Use `describe` per component and
  `it('does x when y')`.
- **Terraform:** `terraform fmt -check` and `terraform validate` pass.

---

## Documentation

README, CLAUDE.md and AGENTS.md stay short. Explanations live in `docs/`, one file per topic, indexed in
[docs/README.md](docs/README.md). A change that alters how something works updates its doc in the same PR. A new
topic gets a new file and an index entry. Keep `AGENTS.md` identical to `CLAUDE.md`.
