# Boilerplate and architecture — Design

**Status:** `Not started` · **Owner:** — · **FSD:** §1, §2, §7 · **Checked against:** FSD V1 · **Depends on:** nothing

The project skeleton every feature is built in: the modules and the direction they may
depend on, the cross-cutting plumbing (configuration, logging, errors, data access, the
event pipeline, the AI-tool connector seam) and the front-end shell. No FSD section owns it;
§1, §2 and §7 constrain it. The rules it settles are written into
[`../architecture.md`](../architecture.md) as each task lands; this plan does not restate
them once they are there.

---

## 1. What the FSD says

- **§1 Working Prototype (MVP)** — "Build a functional prototype capable of connecting to
  at least **one real AI tool** and capturing meaningful usage events. Additional
  integrations may be simulated if required." The skeleton gives every connector, real or
  simulated, the same seam.
- **§2 Architecture Overview** — "Present the end-to-end system architecture showing how
  information flows through the platform: **Discovery → Connection → Event Capture → Data
  Processing → Intelligence → Recommendations → Dashboard**. Clearly explain your design
  decisions and scalability approach." The modules follow that flow, and the decisions
  and the scalability approach are recorded in `architecture.md`.
- **§7 Privacy & Governance** — "Privacy should be a core design principle—not an
  afterthought." It names "Role-based access control" and "Sensitive data protection".
  The skeleton keeps sensitive data out of logs and gives each module a place to check
  roles; the roles themselves are Authentication plumbing's, consent is Privacy &
  Governance's.

The FSD names no language, framework, data store or hosting. The stacks already in the
repository (.NET 10 back end, React 18 + Vite front end) are kept. The data store is one
PostgreSQL database
([`Q1`](../open-questions.md#q1--may-ai-hub-keep-its-data-in-one-postgresql-database), answered);
hosting is Deployment and environments'.

## 2. The decision this rests on

**One deployable back end, split into modules along the §2 flow — a modular monolith.**
Each stage of the flow is a module with its own folder, its own data schema and a public
contract; modules talk through those contracts and through captured events, never through
each other's tables.

- *Rejected: microservices per stage.* A prototype with one team gains nothing from
  network boundaries and pays for them in deployment and debugging; the module
  boundaries keep the option to split later, which is the scalability story §2 asks for.
- *Rejected: one undivided project.* Nothing would stop Recommendations reading Capture's
  tables, and the flow §2 asks us to explain would not be visible in the code.

**Event capture is decoupled from processing.** A captured event is stored first and
processed by a background worker, so a slow or failing processing stage never loses an
event or slows a connector. In the prototype the queue is a table; a broker can replace it
behind the same seam.

## 3. Model

- **Module** — one per stage: Discovery, Connections, Capture, Processing, Intelligence,
  Recommendations, Knowledge, Analytics, Governance. *Invariant:* a module depends only on
  the shared kernel and other modules' contracts — enforced by an architecture test
  (task 1).
- **Usage event** — what Capture stores from a connector: tool, employee, time, kind and
  a payload. *Invariant:* immutable once stored, and stored before any processing —
  enforced by Capture exposing append only (task 4).
- **Connector** — one per AI tool, real or simulated, behind one interface. *Invariant:* a
  connector never writes another module's data; it hands events to Capture (task 5).

## 4. Flows

1. A connector (real or simulated, §1) reads usage from its AI tool and appends usage
   events through Capture's contract.
2. Capture stores each event and acknowledges it — nothing downstream runs inline.
3. The background worker hands stored events to Processing, in order, at least once;
   Processing is idempotent per event. *Decision made here.*
4. Intelligence, Recommendations and Analytics read what Processing produced through its
   contract (§2's flow). Their behaviour is their own features' plans.
5. Every request passes the same pipeline: correlation id, structured log without
   sensitive data (§7), role check where the endpoint requires one, and errors returned
   as problem details.

## 5. Where each piece lives

- `backend/src/App` becomes the host (`App.Api`): composition, endpoints wiring,
  configuration. Each module is a project under `backend/src/Modules/` with its contract
  separate from its implementation; the shared kernel is `backend/src/Shared`.
- `backend/tests/App.Tests` keeps the fast tests and the architecture test; an
  integration test project runs against a real database.
- `frontend/src` gets an app shell: routing, layout, an API client and error display, with
  one folder per feature.
- The placeholder `Greeting` code and its tests are removed once the host replaces them.
- Every later feature: a module in this layout, tests for its behaviour and its role
  checks, and its rules in `architecture.md`.

## 6. Public surface

| Surface | Who may use it | Returns |
|---|---|---|
| `GET /health` | Anyone — the deployment's probe | `200` with the status of the process and the database; `503` when the database is unreachable. Task 2 builds the endpoint and its health-check seam with the process check; task 3 registers the database check |
| `GET /api/openapi.json` | Developers, and the front end's client generation | The OpenAPI document of every endpoint |
| Any `/api/*` failure | — | Problem details (RFC 9457) with a correlation id; never a stack trace or sensitive data |
| The front-end shell at `/` | Anyone; screens behind it check sign-in once Authentication plumbing exists | The layout and navigation every feature's screen lives in; an error screen when the API fails |

## 7. Data and migrations

One PostgreSQL database, one schema per module (decided:
[Q1](../open-questions.md#q1--may-ai-hub-keep-its-data-in-one-postgresql-database)); a module's migrations
touch only its schema. Schema changes are migrations in the repository, applied by the
application at start-up, never by hand. Integration tests run against a real
PostgreSQL in a container, not an in-memory substitute.

- **Access:** EF Core on Npgsql. The shared kernel gives a data-context base bound to a
  schema; each module's context and migrations live in its implementation project, never
  in its `Contracts`.
- **Schema:** named for the module in lower case (`capture`); each module's migration
  history is a table in its own schema, so one module's history never lists another's.
- **Setting:** the connection string is `ConnectionStrings:AppDb`, validated at start-up;
  committed settings hold a placeholder.
- **Migrations:** the host applies each module's pending migrations at start-up, one
  module after another, before it serves; a second start applies nothing. Not by the
  deploy: one path, no hand steps.
- **Health:** `GET /health` carries a `database` check; unreachable → `503`.

## 8. Open questions

None open. [Q1](../open-questions.md#q1--may-ai-hub-keep-its-data-in-one-postgresql-database),
the data store, is answered: one PostgreSQL database, one schema per module.

## 9. Tasks

| # | Task | FSD | Role | Status | Jira | PR | Merged |
|---|---|---|---|---|---|---|---|
| 1 | Back end: host and module layout along the §2 flow, shared kernel, an architecture test that fails on a forbidden dependency; fill `architecture.md` §1–§2 (R-003) | §2 | BE | Built | — | — | 2026-10-15 |
| 2 | Back end: configuration, structured logging with a correlation id and sensitive-data redaction, problem-details errors, `GET /health`, OpenAPI document (R-004) | §2, §7 | BE | Built | — | — | 2026-10-15 |
| 3 | Back end: data access — one PostgreSQL database, schema per module, migrations, integration test project on a real database (Q1) (R-005) | §2 | BE | In progress | — | — | — |
| 4 | Back end: event pipeline seam — Capture's append-only event store and the background worker that hands events to Processing, idempotent per event (R-006) | §2 | BE | In progress | — | — | — |
| 5 | Back end: connector seam — the connector interface and one simulated connector that appends usage events end to end (R-007) | §1 | BE | In progress | — | — | — |
| 6 | Front end: app shell — routing, layout, navigation, an API client typed from the OpenAPI document, error screen (R-009) | §2 | FE | In progress | — | — | — |
