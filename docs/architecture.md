# <!-- FILL: project name --> — Architecture

This document records the architecture of <!-- FILL: project name --> and, more
importantly, **why** each decision was made. It is the answer to "where does this go?"

It describes the code as it stands. Where something is designed but not built, that is
stated in place rather than left to be discovered.

**It owns the reasoning** — why each convention exists, and the trade-offs behind it —
and changes when a decision is made. It states what is true now; superseded history is
in [`archive/architecture-history.md`](archive/architecture-history.md). Which document
owns every other topic is the table in
[`CLAUDE.md`, *Where knowledge lives*](../CLAUDE.md#where-knowledge-lives).

- Features are built from plan documents in [`plans/`](plans/), indexed with their
  status and dependencies in [`project.md`](project.md#part-1--features-and-phasing).
- Questions the requirements have not answered:
  [`open-questions.md`](open-questions.md).

### The *[enforced: X]* convention

A rule a test or check fails the build on is marked *[enforced: X]*, where X names the
test, check or guard rule. The mark is there so you do not trip the rule, not because
anyone has to police it. **A rule without the mark is kept by review**; when a rule is
broken twice, consider enforcing it. The rule is stated in `CLAUDE.md`; the reason for
it is stated here, once.

---

## Contents

| | |
|---|---|
| [1. Layout and layering](#1-layout-and-layering) | the modules, and what may depend on what |
| [2. Where code lives](#2-where-code-lives) | the folder layout, and the rule for placing a new file |
| [3. Conventions](#3-conventions) | each convention, and why it exists |
| [4. Errors](#4-errors) | expected failures versus faults |
| [5. Data](#5-data) | storage, schema changes, the event pipeline, the connector seam, and what is never done by hand |
| [6. Cross-cutting decisions](#6-cross-cutting-decisions) | one table, every technology choice |
| [7. Testing](#7-testing) | the suites, and what each guarantees |
| [8. CI](#8-ci) | every step, and why it is there |
| [9. Adding a new feature](#9-adding-a-new-feature) | the checklist |
| [10. Naming](#10-naming) | what a thing is called, and what it is never called |
| [Dependencies](#dependencies) | when to take a library, and the exit for each risky one |
| [Environments and deployment](#environments-and-deployment) | the environments, and what is still missing |
| [Not built yet](#not-built-yet) | designed, documented, absent from the code |
| [Known trade-offs](#known-trade-offs) | deliberate compromises, and what would change them |

---

## 1. Layout and layering

The back end is a **modular monolith**: one deployable host, split into modules along the
FSD §2 flow — Discovery → Connection → Event Capture → Data Processing → Intelligence →
Recommendations → Dashboard. The modules are Discovery, Connections, Capture, Processing,
Intelligence, Recommendations, Knowledge, Analytics and Governance; each is a contract
project and an implementation project.

| Project | Contains | May depend on |
|---|---|---|
| `App.Api` (host) | Composition root: registers every module, endpoints wiring, configuration | Anything |
| `App.Shared` (shared kernel) | Types every module needs, such as `IModule` | Nothing of ours |
| `App.<Module>.Contracts` | The module's public surface — the only part other modules see | `App.Shared`, other modules' contracts |
| `App.<Module>` | The module's implementation | `App.Shared`, its own and other modules' contracts |

**A module depends only on the shared kernel and other modules' contracts** — never on
another module's implementation or the host; the shared kernel depends on no module and
not the host *[enforced: `ArchitectureTests` in `backend/tests/App.Tests/Architecture`]*.

**Why:** one team and one prototype gain nothing from network boundaries between stages
(microservices), but one undivided project lets Recommendations read Capture's internals.
Contract-only edges keep each stage replaceable and the §2 flow visible in the code, and
keep the option to split a module out later — the scalability approach §2 asks for. The
reasoning in full is [the plan's §2](plans/boilerplate-and-architecture.md#2-the-decision-this-rests-on).

## 2. Where code lives

```
backend/
  App.slnx
  src/App/                    App.Api — the host (Program.cs is the composition root)
  src/Shared/                 App.Shared — the shared kernel
  src/Modules/<Module>/
    App.<Module>.Contracts/   what other modules may use
    App.<Module>/             everything else
  tests/App.Tests/            fast tests, including the architecture test
```

- **A type another module needs** goes in the owning module's `Contracts` project — e.g.
  Capture's append-events interface in `backend/src/Modules/Capture/App.Capture.Contracts`.
- **A type only its module uses** goes in the implementation project, e.g. Capture's
  storage in `backend/src/Modules/Capture/App.Capture`.
- **A type every module needs and no module owns** goes in `backend/src/Shared`. Keep it
  small; a type with one owner belongs to that module.
- **Wiring** — registering a module, mapping its endpoints — goes in the host,
  `backend/src/App`.
- **A new module** is a folder under `backend/src/Modules/` with both projects, added to
  `App.slnx` and to `DependencyRule.Modules` in the architecture test.

### The front end

```
frontend/src/
  App.tsx            the one route table: every screen is a child of the shell's layout
  shell/             layout, navigation, not-found and the error state — around every screen
  features/<area>/   one folder per navigation area; its screens and what only they use
  api/               the API client, its generated types (schema.d.ts) and useApiRequest
  ui/                the design tokens (tokens.css) and shared components
  testing/           test infrastructure (the Vitest environment)
```

- **A screen** goes in its area's `features/<area>/` and gets its route in `App.tsx`; a new
  area also gets its link in `shell/Navigation.tsx`. A feature never imports another
  feature's folder: what two features share goes in `ui/` or `api/`.
- **The API is called only through `api/client.ts`** — `apiGet(path)` takes only paths
  the generated `paths` type lists, so a call to a path the back end does not serve fails
  `npm run typecheck`. **Why:** types written by hand drift from the back end; generated
  ones break the build instead. `src/api/schema.d.ts` is generated from
  `GET /api/openapi.json` by `npm run generate:api` and committed, so the build needs no
  running back end; regenerate it in the change that moves the API.
- **Styles use the tokens** in `ui/tokens.css` (light and dark from the operating system,
  `data-theme` overrides); no CSS framework ([`ui-standard.md` §5](ui-standard.md#5-consistent--one-small-design-system)).

## 3. Conventions

One subsection per convention: **the rule, then why.** A rule whose reason is not
written down gets removed by the first person it inconveniences.

### Every request carries a correlation id

The host's first middleware (`backend/src/App/Hosting/CorrelationIdMiddleware.cs`) takes
the caller's `X-Correlation-Id` — if it is 1–128 characters of `A–Z a–z 0–9 . _ : -` —
or generates one, echoes it on the response, and opens a log scope carrying
`CorrelationId` *[enforced: `CorrelationIdTests`]*. **Why:** one id ties a caller's
report, the response and every log entry of the request together; the character limit
keeps a caller from forging or bloating a log line.

### Sensitive data never reaches a log

Every logger provider is wrapped by `RedactingLoggerProvider`
(`backend/src/App/Hosting/Logging/`), so a sink sees only redacted messages, structured
properties, scopes and exception text. A property whose key names a password, secret,
token, authorization, API key, connection string, credential, cookie or email is replaced
by `[REDACTED]`; email addresses, `Bearer …` credentials and `password=…`-style
assignments are masked wherever they appear. The request log names the method, path and
status only — never the query string or headers *[enforced: `RedactionTests`]*. **Why:**
FSD §7 "Sensitive data protection"; masking by key catches what a caller forgot to tag,
which call-site data classification would not.

### Settings are validated at start-up, and committed settings hold no secrets

Each settings section binds to an options class with data annotations and
`ValidateOnStart()`; a missing or invalid value stops the host with an error naming the
setting, never its value. A secret-bearing key in `appsettings*.json` holds a placeholder
(`CHANGE_ME`, `<…>`, `__…__`) and the real value comes from the environment
*[enforced: `SettingsTests`]*. **Why:** a misconfigured host fails at deploy, not on the
first request that needs the setting.

| Setting | Required | Meaning |
|---|---|---|
| `Api:Title`, `Api:Version` | Yes | The OpenAPI document's title and version |
| `Logging:LogLevel:*` | No | Log levels by category; `Microsoft.AspNetCore` is `Warning` so hosting's request lines, which carry the query string, are not written; `Microsoft.EntityFrameworkCore` is `Warning` so each SQL command is not logged |
| `ConnectionStrings:AppDb` | Yes | The one PostgreSQL database (§5). Committed settings hold `Password=REPLACE_<password>`; the real value comes from the environment (`ConnectionStrings__AppDb`) *[enforced: `DatabaseSettingsTests`, guard]* |
| `Database:MigrateAtStartup` | No (default `true`) | Apply each module's migrations at start-up (§5); `false` only in the fast tests, which have no database |

### Coding principles (every stack)

The rules are in [`CLAUDE.md`](../CLAUDE.md#coding-principles-every-stack); the reviewer
checks every change against them. Why each one:

- **Less code, inside the boundaries.** Every line is read, tested and kept by someone
  after it is written, so code nobody asked for costs without paying back: an option
  nobody sets is a path nobody tests, and an interface with one implementer is two
  places to change instead of one. A boundary costs a few lines and keeps a change in
  one layer from reaching every other, so when the two collide the boundary wins.
- **Neat and understandable.** Code is read far more often than it is written, by people
  and by the AI that changes it next. A name that says what a thing does cannot drift
  from it the way a comment can; a short function with one job can be tested and
  replaced on its own; an early return keeps the main path at the left margin. A named
  constant says what a number means, and changes in one place.
- **Efficient by design.** The costly mistakes are in the shape of the code, not its
  speed: a query inside a loop runs fast on ten test rows and times out on ten thousand
  real ones, and no later tuning fixes the shape. Below that, a guess about speed is
  usually wrong, so a tuning change waits for a measurement.
- **Reusable, without being clever.** A rule written in two places drifts until the two
  disagree. But an abstraction made from one example guesses at the second and usually
  guesses wrong; by the third, the common part is known. A maintained library carries
  its own tests and fixes, and behind one file it can be replaced.
- **The basics.** Each closes a door a past project left open: input trusted from
  outside, an error swallowed to keep a request alive, a secret in a log, a shared
  object changed from two places, a class with three reasons to change, business rules
  that change when the database does, and a change with nothing to prove it works.

## 4. Errors

**Any failure under `/api/*` returns RFC 9457 problem details**
(`application/problem+json`: `type`, `title`, `status`) with a `correlationId` member
equal to the response's `X-Correlation-Id` *[enforced: `ProblemDetailsTests`]*.

- **A fault** — an unhandled exception — is caught by the exception handler, logged
  (redacted) and returned as `500`. The body carries no `detail`, exception message or
  stack trace: those can hold data the caller must not see.
- **A status with no body** under `/api/*` (an unknown path's `404`) is written as
  problem details by the status-code pages.
- **An expected failure** (validation, not found, conflict) is returned by the endpoint
  as problem details with its own status; it is not thrown.

**In the front end, an API call never throws.** `apiGet` returns
`{ ok: true, data }` or `{ ok: false, status?, title?, correlationId? }`: problem details
give the status, title and correlation id; a network error or a body that is not JSON is a
failure with none of them. A screen loads through `useApiRequest(request)`
(`{ data, failed, retry }`) and, when `failed`, shows `shell/ErrorState` in its main
region — a plain message and *Try again*, never the title, status, `detail` or
correlation id; the correlation id goes to the browser console for support. An unknown
address shows *Page not found* inside the shell. **Why:** a failed request is expected,
not a fault, and the person needs a way out, not a code
([`ui-standard.md` §2](ui-standard.md#2-easy-for-people-who-are-not-technical)).

## 5. Data

**One PostgreSQL database, one schema per module**, named for the module in lower case
(`capture`) ([Q1](open-questions.md#q1--may-ai-hub-keep-its-data-in-one-postgresql-database)).
Access is EF Core on Npgsql.

- **A module's data context** derives from `ModuleDbContext<T>` in the shared kernel
  (`backend/src/Shared/Data`), which puts everything it maps in the module's schema. The
  context and its migrations live in the module's implementation project (`Data/`,
  `Data/Migrations/`), never in its `Contracts` *[enforced: `DataAccessBoundaryTests`]*.
  **Why:** another module reaches the data only through the owner's contract.
- **Each module's migration history** is `<schema>.__ef_migrations_history`, so no
  history lists another module's migration and a module's migrations touch only its schema.
- **The host registers each context** in `Program.cs` (`AddModuleDbContext<T>`) and
  **applies every module's pending migrations at start-up**, in that order, before it
  serves (`DatabaseMigrator`). Applying them again changes nothing. **No schema change
  is made by hand**; a change is a new migration in the module.
- No schema doc is generated yet.

### The event pipeline: store first, hand over at least once, process once

The Event Capture → Data Processing hop of the FSD §2 flow
([plan §4](plans/boilerplate-and-architecture.md), flows 2–3):

- **Capture's contract is append-only.** `IUsageEventLog.AppendAsync` validates a
  `UsageEvent` (tool, employee, time, kind, JSON payload), stores it in
  `capture.usage_events` and returns its id; nothing downstream runs inside the call. The
  contract has no update or delete *[enforced: `EventPipelineRuleTests`]*, and no code
  changes a stored row.
- **Capture's worker hands events over at least once.** `UsageEventWorker`, a hosted
  service in `App.Capture`, reads events past its cursor (`capture.hand_over_cursors`) in
  store order and calls `IUsageEventProcessor` in `App.Processing.Contracts`; it advances
  the cursor only after Processing returns. A failure is logged with the event id — never
  the payload — and the same event is retried before any later one; a restart resumes from
  the cursor.
- **Processing is idempotent per event.** It records each event id in
  `processing.processed_events` in the same transaction as the handling's effects, and
  skips an id already there, so a redelivery changes nothing.

**Why:** a connector's event is safe the moment it is stored, whatever Processing is
doing; Processing's failures and restarts cost a retry, never an event, and at-least-once
delivery with idempotent handling is simpler and sturdier than exactly-once.

**The queue is a table** — the scalability approach §2 asks for. One PostgreSQL holds
the events and the cursor, so the prototype needs no broker to run or operate. Appends
take a transaction-level advisory lock so store order is commit order and the cursor can
never pass an event that commits late; that serialises appends, and one failing event
holds back those after it (see [Known trade-offs](#known-trade-offs)). **A message broker
(e.g. RabbitMQ, Kafka) replaces the table behind the same two contracts** when append
volume or several consumers need it: `IUsageEventLog` publishes, the worker becomes a
subscriber, and Processing's idempotency already absorbs a broker's redelivery.

### The connector seam: one interface, writes only through Capture

The Connection → Event Capture hop of the FSD §2 flow
([plan §3 *Connector*](plans/boilerplate-and-architecture.md), flow 1):

- **One interface for every connector, real or simulated.** `IConnector` in
  `App.Connections.Contracts` names the tool it serves and has one `RunAsync`, which returns a
  `ConnectorRunResult`: the events appended and, per refused record, its position and the field
  Capture named. A simulated tool (`SimulatedConnector`, reading `SimulatedUsage`) and a real
  one implement the same interface; the host registers them (`AddConnectors`).
- **A connector writes only through Capture's contract, never another module's data.** It
  appends each record as a `UsageEvent` through `IUsageEventLog`; `App.Connections` references
  `App.Capture.Contracts`, never `App.Capture` *[enforced: `ConnectorSeamTests`]*. A record
  Capture refuses is reported, not fatal: the run goes on with the rest.
- **A run logs the tool and its counts** — never an employee or a payload.

**Why:** a new AI tool is one more `IConnector`, and what it captures enters the pipeline at
the one place that validates and stores events, so no connector can bypass Capture or corrupt
another module's data.

## 6. Cross-cutting decisions

| Concern | Decision | Why |
|---|---|---|
| Configuration | Options classes bound from configuration, validated with data annotations at start-up (§3) | First-party; fails at start, not on first use |
| Logging | Built-in `ILogger`, JSON console formatter with scopes, every provider wrapped by the redacting provider (§3) | First-party; one wrapper redacts for every sink, including ones added later. *Rejected: Serilog* — a dependency for what the built-in logger does; *rejected: `Microsoft.Extensions.Compliance.Redaction`* — it only redacts values tagged at the call site |
| Correlation id | `X-Correlation-Id`, accepted or generated, echoed, in every log entry of the request (§3) | Ties a report, a response and its logs together |
| Errors | `AddProblemDetails`, exception handler, status-code pages for `/api/*` (§4) | One error shape for every caller, with no fault detail leaked |
| Health | `GET /health` — anonymous, `200` with `status` and each check's status, `503` when a check is unhealthy; checks are registered on `AddHealthChecks()` in `HostPlumbing`: `process` and `database` (`AspNetCore.HealthChecks.NpgSql`). The body carries names and statuses only, never a connection string | The deployment's probe |
| API document | `Microsoft.AspNetCore.OpenApi`, served at `GET /api/openapi.json` | First-party; the front end's client is generated from it |
| Front-end routing | `react-router` (library mode, `createBrowserRouter`), one route table in `App.tsx` | Back, forward, deep links and `aria-current` are the library's, not ours to get right. *Rejected: a hand-written router on `history.pushState`* |
| Front-end API client | `openapi-typescript` generates `src/api/schema.d.ts`; `openapi-fetch` makes the calls behind `src/api/client.ts` (§2, §4) | Types follow the back end's document. *Rejected: hand-written fetch types* — they drift |
| Front-end dev server | Vite proxies `/api` to `http://localhost:5000` (`vite.config.ts`) | The app calls the API on its own origin in development as in production |
| Front-end serving | The host serves the built front end from its web root (`UseDefaultFiles`, `UseStaticFiles`) and answers any other path that is not a file and not under `/api` with `index.html` (`MapFallbackToFile`, in `HostPlumbing`). The fallback has the lowest priority, so every endpoint and `/health` win, and an unknown `/api/*` path stays a problem-details `404`. With no front end in the web root it serves nothing and the host still starts | One origin, so no CORS and one deployable. *Rejected: a separate web server (e.g. nginx) beside the host* — a second deployable |

## 7. Testing

| Suite | Covers | Needs | Command (in `backend/` unless named) |
|---|---|---|---|
| `tests/App.Tests` | Host plumbing (settings, logging, errors, health process check, OpenAPI) and the architecture rules | Nothing: migrations are off and the `database` check is dropped (`Host/TestHost`) | `dotnet test tests/App.Tests` |
| `tests/App.IntegrationTests` | The real host on PostgreSQL in a container (Testcontainers, `postgres:17-alpine`): schemas, migrations and their histories, the database health check, the event pipeline (`EventPipeline/`) | A container runtime (Docker) | `dotnet test tests/App.IntegrationTests` |
| Front end, `src/**/*.test.ts(x)` | The shell, screens and API client (Vitest, Testing Library, jsdom); requests by stubbing the global `fetch` | Nothing | `npx vitest run` (in `frontend/`) |

The front end's tests run in `src/testing/jsdom-environment.ts`: Vitest's jsdom, keeping
Node's `AbortController` and `AbortSignal`, because Node's `Request` rejects jsdom's
signal and react-router builds one on every navigation.

One feature's tests: `--filter "AC=<criterion id>"` (front end: `-t "AC <criterion id>"`). **A missing container runtime fails
the integration suite** — the fixture throws before any test — it never skips
*[enforced: `ContainerRuntimeTests`]*. **Why:** a skipped suite reads as a pass.

## 8. CI

There is no CI file yet: every check is one script, `python3 devtools/checks/run-checks.py`, run at
the repository root on every branch before a merge, and the command a pipeline will call. It runs the
steps below in order, stops at the first failure and names it on its last line (exit `1`); exit `0`
means every step passed.

| Step | What it does | Why |
|---|---|---|
| **Build** | `dotnet build App.slnx` in `backend/`, then `npm run build` in `frontend/` | Warnings are errors, so a warning cannot accumulate |
| **Tests** | `dotnet test` of `tests/App.Tests` and `tests/App.IntegrationTests` (`.trx`), then `npx vitest run` (JUnit), then `npm run lint` and `npm run typecheck`; each suite's results go through `check-test-results.py`, and its line prints executed / passed / failed / skipped | A suite that skipped a test fails as skipped, one that ran nothing or wrote no results fails as empty: a missing database or a filter matching nothing never looks like a pass. Without Docker the integration suite fails |
| **Generated docs match the source** | `gen-task-rollup.py --check` and `factory.py index --check` | A hand-edited or stale generated doc is caught before merge |
| **Factory self-tests** | `factory.py verify --all`, `check-book.py`, `check-rules.py` and `check-code-rules.py`; the factory has no unittest files yet | The guard is a first layer; CI re-checks what reaches a pull request |
| **No secrets in settings** | <!-- FILL --> | <!-- FILL --> |
| **Dependencies** | <!-- FILL: licence and vulnerability checks --> | <!-- FILL --> |

## 9. Adding a new feature

1. **A row in [`project.md`](project.md#part-1--features-and-phasing)** and a plan from
   [`plans/_template.md`](plans/_template.md), before any code.
2. **Questions the requirements leave open** go to
   [`open-questions.md`](open-questions.md) with a recommendation.
3. <!-- FILL: the build order — which layer first, and what compiles before the next
   step. -->
4. **Tests that prove the behaviour**, including <!-- FILL: the project's mandatory
   cases, e.g. authorization, isolation between tenants -->.
5. **Regenerate the generated docs** the change reaches: <!-- FILL -->.
6. **Update the owning docs in the same commit** — the plan, the feature's status in
   `project.md`, and <!-- FILL: any guide for consumers of the feature -->.

## 10. Naming

<!-- FILL: what each kind of thing is called, and the names never used (e.g. a type
whose last word is Helper, Manager or Utility). *[enforced: X]* where it is. -->

---

## Dependencies

**Prefer a maintained open-source library over writing our own**; write our own only
when the surface is small, the dependency carries risk, and it stays behind one file.
**Every third-party library stays isolated to one place.**

| Library | Licence | Where it is isolated | Exit if it has to go |
|---|---|---|---|
| EF Core + `Npgsql.EntityFrameworkCore.PostgreSQL` | MIT / PostgreSQL | `backend/src/Shared/Data` and each module's `Data/` | Another data access library behind each module's context; the schema-per-module rule stays |
| `AspNetCore.HealthChecks.NpgSql` | Apache-2.0 | One line in `HostPlumbing` | A hand-written `SELECT 1` check |
| `Testcontainers.PostgreSql` | MIT | `backend/tests/App.IntegrationTests` | A PostgreSQL started by the CI job |
| `react-router` | MIT | `frontend/src/App.tsx` (route table) and `frontend/src/shell/` (`Outlet`, `NavLink`, `Link`) | Another router behind the same route table |
| `openapi-fetch` | MIT | `frontend/src/api/client.ts` | `fetch` in the same file; `apiGet`'s signature stays |
| `openapi-typescript` (dev) | MIT | `npm run generate:api` → `frontend/src/api/schema.d.ts` | Another generator writing the same `paths` type |

## Environments and deployment

**One image of both halves**, built by the root `Dockerfile` in three stages: the front end
(`npm ci`, `npm run build`), the back end (`dotnet publish`), and an `aspnet` runtime holding the
published host with the front end's `dist/` as its `wwwroot`, running as the image's non-root user
on port `8080`. It is tagged with the commit it was built from
(`docker build -t ai-hub:$(git rev-parse --short HEAD) .`, [README](../README.md#the-image)), and
configuration reaches it as environment variables (`ConnectionStrings__AppDb`). The root
`.dockerignore` keeps `.git`, `.factory/`, `node_modules/`, `bin/`, `obj/`, `dist/` and `.env*`
files out of the build context. **Why:** the same artefact runs everywhere, and its tag names the
code in it.

**Local: Compose at the repository root.** `compose.yaml` runs the commit's image (`ai-hub:${AI_HUB_TAG}`;
Compose never builds it, so what runs is what was built and tested) beside `postgres:17-alpine`
(PostgreSQL 17), whose data lives on the named volume `ai-hub-data` and so survives `docker compose down`
(`down -v` removes it). The app answers on `http://localhost:8080` once the database is healthy; the
database publishes no port. Every value comes from an uncommitted, git-ignored `.env`, copied from the
committed `.env.example` whose secrets are placeholders; `compose.yaml` holds only `${VAR:?…}`
references, so a missing value stops Compose before any container starts, and the password is written
once — the app's connection string is composed from it. How to run it: [README](../README.md#run-it-locally-with-compose).
**Why:** secrets stay out of the repository and the image (FSD §7), and the prototype runs as one thing (§1).

**Demo: one script deploys a commit.** On the demo machine, `devtools/deploy.py <commit> <env-file>`
builds `ai-hub:<commit>` from that commit (exported with `git archive`, so the working tree is not
touched), starts it with the same `compose.yaml` as Compose project `ai-hub-demo`, its values from an
environment file kept outside the repository, and waits for `GET /health` on that file's `AI_HUB_PORT`
to answer `200` (`--wait`, 180 s by default). When no `200` comes, it starts the previous image again,
waits for it, and exits non-zero naming the failure; with no previous image it says so. A build that
fails stops before any container is touched. It never removes the `ai-hub-data` volume — migrations
only add, so the previous image runs on the newer schema — and prints neither the environment file's
path nor its values, nor Docker's own output from Compose. Run it for a commit on `main` that passed
the [checks script](#8-ci). How: [README](../README.md#deploy-a-commit-to-the-demo-server). **Why:** the
demo runs a known build, and a bad one never leaves it down (FSD §8; Q2, Q3).

## Not built yet

<!-- FILL: designed and documented here, absent from the code. Delete an entry when it
is built. -->

## Known trade-offs

Compromises **considered and deliberately not fixed**. Each names what would change the
decision, so it is revisited on evidence rather than on someone's memory. A defect
surprised us; a trade-off is a thing we chose.

| Trade-off | Why it stands | Revisit when |
|---|---|---|
| Provider-specific log filters (`Logging:Console:LogLevel`) do not apply: every provider is wrapped by the redacting provider, so filter rules match only by category | Redaction for every sink matters more than per-sink levels; one console sink needs none | A second production sink needs its own levels |
| The production path is presented, not built — FSD §8's "Production scalability considerations": the demo runs on one machine with Compose and no cloud provider (Q2). Production would run the same image on a container platform of the chosen provider, scaled out behind its load balancer (the app keeps no state in memory), with PostgreSQL as a managed database (backups, failover) in place of the Compose container, secrets from the provider's secret store, and the checks and deploy scripts called by a hosted pipeline (Q3) | No provider is chosen; one image and a connection string move to any of them unchanged, and one machine carries a prototype demo | A provider is chosen, or real users or data need more than one machine — the event queue's row above is part of the same step |
| The usage-event queue is a PostgreSQL table ([§5](#the-event-pipeline-store-first-hand-over-at-least-once-process-once)): appends are serialised by an advisory lock, and an event Processing keeps failing holds back every later one (retried each second, logged each time) | No broker to run in the prototype; strict store order with no lost event; a stuck event is loud in the logs | Append volume contends on the lock, a second consumer needs the events, or a poison event needs parking — move to a broker behind the same contracts |
