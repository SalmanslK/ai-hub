# AI-Hub

An Enterprise AI Intelligence Platform ([what it is](docs/system-overview.md)): a .NET 10 back end in
`backend/` and a React + TypeScript front end in `frontend/` ([how they are built](docs/architecture.md)).

## Back end

In `backend/`, with PostgreSQL reachable through `ConnectionStrings__AppDb`
([settings](docs/architecture.md#settings-are-validated-at-start-up-and-committed-settings-hold-no-secrets)):

| To | Run |
|---|---|
| Build | `dotnet build App.slnx` |
| Run (on `http://localhost:5000`) | `dotnet run --project src/App` |
| Fast tests | `dotnet test tests/App.Tests` |
| Integration tests (needs Docker) | `dotnet test tests/App.IntegrationTests` |

## Front end

In `frontend/`, after `npm ci`:

| To | Run |
|---|---|
| Run (on `http://localhost:5173`; `/api` goes to the back end) | `npm run dev` |
| Build | `npm run build` |
| Tests, lint, types | `npx vitest run`, `npm run lint`, `npm run typecheck` |
| Regenerate the API client's types from the back end's OpenAPI document (with the back end running) | `npm run generate:api` |

`npm run generate:api` rewrites `src/api/schema.d.ts` from `GET /api/openapi.json`; commit it with the
back-end change that moved the API, so the build needs no running back end.

## Run every check before a merge

At the repository root, with Docker running and `npm ci` done in `frontend/`:
`python3 devtools/checks/run-checks.py` ([what it runs](docs/architecture.md#8-ci)). It prints one line
per step and exits `0` only when every step passed; otherwise its last line names the step that failed.

## The image

One image holds both halves: the host serves the built front end on the same origin as `/api`
([why](docs/architecture.md#environments-and-deployment)). At the repository root, with Docker running:

| To | Run |
|---|---|
| Build it, tagged with the commit | `docker build -t ai-hub:$(git rev-parse --short HEAD) .` |
| Run it (on `http://localhost:8080`) | `docker run -p 8080:8080 -e ConnectionStrings__AppDb="<connection string>" ai-hub:<commit>` |

## Run it locally with Compose

The image beside PostgreSQL 17, its data on a named volume
([the local environment](docs/architecture.md#environments-and-deployment)). At the repository root,
after building the image:

1. `cp .env.example .env`, then in `.env` set `AI_HUB_TAG` to the image's commit and replace every
   `CHANGE_ME` with a real value. `.env` is git-ignored: never commit it.
2. `docker compose up -d` — the app answers on `http://localhost:8080` (`/health` returns `200`).

`docker compose down` stops it and keeps the data; `docker compose down -v` deletes the data too.

## Deploy a commit to the demo server

On the demo machine, at the repository root with the commit fetched and Docker running
([how it works](docs/architecture.md#environments-and-deployment)):

1. Once: copy `.env.example` to a file **outside the repository** and fill it as for Compose
   (`AI_HUB_TAG` is set by the script; `AI_HUB_PORT` is where the demo answers).
2. `python3 devtools/deploy.py <commit> <path to that file>` — for a commit on `main` that passed every
   check. It builds `ai-hub:<commit>`, starts it as Compose project `ai-hub-demo` and exits `0` once
   `/health` answers `200`. If it never does (`--wait <seconds>`, 180 by default), it puts the previous
   image back and exits non-zero; the data is kept either way.
