# Deployment and environments — Design

**Status:** `In progress` · **Owner:** — · **FSD:** §1, §2, §7, §8 · **Checked against:** FSD V1 · **Depends on:** Boilerplate and architecture

Where AI-Hub runs and how a build reaches it: one container image, PostgreSQL beside it,
two environments, one script that runs every check and one that deploys. No FSD section
owns it; §1, §2, §7 and §8 constrain it. The rules it settles are written into
[`../architecture.md`](../architecture.md) (§8 CI, *Environments and deployment*) as each
task lands; this plan does not restate them once they are there.

---

## 1. What the FSD says

- **§1 Working Prototype (MVP)** — "Build a functional prototype capable of connecting to
  at least **one real AI tool** and capturing meaningful usage events." The prototype has
  to run somewhere other than a developer's machine to be shown working.
- **§2 Architecture Overview** — "Clearly explain your design decisions and scalability
  approach." The deployment is one of those decisions.
- **§7 Privacy & Governance** — the solution should demonstrate "Sensitive data
  protection". Secrets and data in each environment stay out of the image, the repository
  and the logs.
- **§8 Demo & Documentation** — "Each team will present: A **5–7 minute live
  demonstration** … Architecture overview … Key assumptions … Future enhancements …
  Production scalability considerations." The demo runs on a known build in a known
  place; the production path is presented, not necessarily built.

The FSD names no hosting, cloud provider, environments, CI or delivery path. This plan
rests on the answers to
[Q2](../open-questions.md#q2--may-ai-hub-run-as-containers-on-one-server-with-no-cloud-provider-chosen-yet)
and
[Q3](../open-questions.md#q3--may-a-person-deploy-to-the-demo-server-by-running-one-script-after-a-merge-with-every-check-in-one-script-of-its-own).

## 2. The decision this rests on

**(Q2) One container image holding the back end and the built front end, run
beside PostgreSQL by Docker Compose.** The host serves the front end's static files on the
same origin as `/api`, as the development proxy already pretends
([architecture §6](../architecture.md#6-cross-cutting-decisions)), so there is no CORS, no
second server and one thing to deploy.

- *Rejected: a separate web server or CDN for the front end.* A second deployable and a
  cross-origin API for no gain at prototype scale; it stays a scalability step to present
  (§8).
- *Rejected: a managed cloud platform now.* No provider is chosen (Q2); containers move to
  any of them unchanged, which is the scalability story §2 and §8 ask for.

**(Q3) Every check in one script, the deploy in another.** With no shared
repository server there is nothing for a hosted pipeline to run on; the scripts are what a
pipeline calls once there is one.

## 4. Flows

1. **Check** (Q3) — before a merge to `main`, the checks script builds both
   halves (warnings are errors), runs every suite — fast, integration on PostgreSQL in a
   container, front end — then lint, types, every generator's `--check` and the factory
   self-tests. A suite that skips or runs nothing fails it (CLAUDE.md, *Report what
   ran*).
2. **Build** — the image is built in stages: the front end with `npm ci` and
   `npm run build`, the back end with `dotnet publish`, then the runtime image with the
   published host and the built front end. It is tagged with the commit it came from.
   *Decision made here.*
3. **Deploy** (Q2, Q3) — a person runs the deploy script for a commit on
   `main`; it builds that commit's image, starts it on the demo server with Compose and
   waits for `GET /health` to answer `200`.
4. **Start** — the host validates its settings, applies each module's pending migrations
   and only then serves (already built:
   [Settings](../architecture.md#settings-are-validated-at-start-up-and-committed-settings-hold-no-secrets),
   [§5 Data](../architecture.md#5-data)). A missing setting stops it before it serves.
5. **Fail** — no `200` from `/health` within the deploy's wait: the script puts the
   previous image back and exits non-zero. Migrations only add, so the previous image
   still runs on the newer schema. *Decision made here.*

## 5. Where each piece lives

- **The image:** a `Dockerfile` at the repository root, beside a `.dockerignore`; the host
  serves the built front end from its web root and falls back to `index.html` for any
  path that is not `/api/*` or `/health`, so a deep link loads the app and an unknown API
  path is still problem details.
- **The environments:** `compose.yaml` at the root runs the image and `postgres:17-alpine`
  (the version the integration tests use) with a named volume for the data. Secrets come
  from an environment file outside the repository; the committed example holds
  placeholders only.
- **The scripts:** under `devtools/`, one for the checks and one for the deploy, each
  usable from a terminal and from a future pipeline.
- **Already built, used rather than recreated:** `GET /health` as the probe; migrations at
  start-up; settings from the environment (`ConnectionStrings__AppDb`); JSON console logs
  with redaction — the container's log is stdout.
- **Obligations:** each task fills its part of `architecture.md` (§8 CI, *Environments
  and deployment*, *Known trade-offs* for the production path) and `README.md` (how to run
  it), and ships the tests that prove it.

## 6. Public surface

| Surface | Who may use it | Returns |
|---|---|---|
| The app at `/` on the demo server and in the local Compose environment | Anyone who can reach it; screens behind it check sign-in once Authentication plumbing exists | The front end, from the same origin as `/api`; any non-API path returns the app so deep links work |
| `GET /health` (already built) | Anyone — the deploy's and the container's probe | `200` with each check's status; `503` when the database is unreachable |
| The checks script (Q3) | Developers and the factory, before a merge | Exit `0` when every build, suite and check passes; non-zero naming the step that failed, including a skipped or empty suite |
| The deploy script (Q2, Q3) | A person, for a commit on `main` | Exit `0` once the new image answers `/health` with `200`; non-zero with the previous image restored otherwise |

No screen is added, so [`../ui-standard.md`](../ui-standard.md) does not apply.

## 7. Data and migrations

No schema of its own. The database lives in a named Compose volume, so a redeploy keeps
the data; the image never holds data or secrets. Migrations stay the host's, at start-up
(decided by Boilerplate and architecture) — the deploy runs no database step.

## 8. Open questions

- [Q2](../open-questions.md#q2--may-ai-hub-run-as-containers-on-one-server-with-no-cloud-provider-chosen-yet)
  — containers on one server, two environments, no cloud provider yet. Answered; tasks 1, 2,
  4 and 5 build on it.
- [Q3](../open-questions.md#q3--may-a-person-deploy-to-the-demo-server-by-running-one-script-after-a-merge-with-every-check-in-one-script-of-its-own)
  — one checks script and one deploy script, no hosted pipeline yet. Answered; tasks 3 and 5
  build on it.

## 9. Tasks

| # | Task | FSD | Role | Status | Jira | PR | Merged |
|---|---|---|---|---|---|---|---|
| 1 | Back end: the host serves the built front end on its own origin, with `index.html` for any non-API path; the multi-stage `Dockerfile` and `.dockerignore` that build one image of both halves, tagged by commit (Q2) (R-011) | §1, §2 | BE | In progress | — | — | — |
| 2 | Back end: local environment — `compose.yaml` running the image beside PostgreSQL 17 on a named volume, secrets from an uncommitted environment file with a committed placeholder example; fill `architecture.md` *Environments and deployment* and `README.md` (Q2) (R-012) | §1, §7 | BE | In progress | — | — | — |
| 3 | Back end: the checks script — builds, every test suite, lint, types, generators' `--check`, factory self-tests, failing on a skipped or empty suite; fill `architecture.md` §8 CI, so the build the demo runs has passed every check (Q3) (R-013) | §1, §8 | BE | In progress | — | — | — |
| 4 | Demo server: a host with a container runtime, reachable for the demo, and its environment file with real secrets | §8 | DevOps | Built | — | — | — |
| 5 | Back end: the deploy script — build a commit's image, start it on the demo server, wait for `/health`, restore the previous image on failure; record the production path (provider, scaling, managed database) in `architecture.md` *Known trade-offs* as §8's scalability considerations (Q2, Q3) (R-014) | §8 | BE | In progress | — | — | — |

**Task 4, as set up (20 Oct):** the demo server is the presenter's own machine, running Docker; the demo opens
`http://localhost:8088`. It runs as its own Compose project, `ai-hub-demo`, so its containers and data volume never
meet the local environment's (task 2, port 8080). Its environment file holds `.env.example`'s keys with a real
password and `AI_HUB_PORT=8088`; it lives outside the repository, at a path the person keeps to themselves, and
the deploy script (task 5) is given that path — never a path written here. Proven by hand with
`docker compose -p ai-hub-demo --env-file <file> up -d --wait` on an image of `main`: `/health` and `/` answered `200`.
