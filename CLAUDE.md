# AI-Hub

AI-Hub is an Enterprise AI Intelligence Platform: it discovers the AI tools a workforce uses, connects them with
each employee's consent, captures usage events, and turns them into insights, recommendations, a knowledge hub
and adoption analytics ([what it is](docs/system-overview.md)). This repository holds a **.NET 10 back end** in
`backend/` (a modular monolith, `backend/App.slnx`) and a **React + TypeScript front end** (Vite, Vitest) in
`frontend/`; how they are built is the [architecture guide](docs/architecture.md). It is built by the AI factory,
one run per task, from the FSD in `requirements/fsd/`.

## How these instructions are organised

This file holds the rules that apply everywhere. **Nested `CLAUDE.md` files load when
you work in their folder — read the one for where you are before changing anything
there:**

| Folder | Its rules |
|---|---|
| `docs/` | [`docs/CLAUDE.md`](docs/CLAUDE.md) — writing docs: current state vs history, defects, questions, plans, generated files |

*[enforced: X]* marks a rule a test or check fails the build on; it is written here
so you do not trip it, not because anyone has to police it.

## Layout

| Path | Contains |
|------|----------|
| `backend/src/` | All back-end code: the host (`App`), the shared kernel (`Shared`) and one folder per module under `Modules/` ([where code lives](docs/architecture.md#2-where-code-lives)). |
| `backend/tests/` | All back-end tests, including the architecture test. |
| `frontend/src/` | All front-end code, with its tests beside it (`*.test.ts`, `*.test.tsx`). |
| `docs/` | The documentation — owners in [Where knowledge lives](#where-knowledge-lives). |
| `requirements/` | **Read-only.** The client's or product's requirement documents, as snapshots. The guard refuses edits (`[guard].read_only` in `devtools/factory/factory.toml`). |
| `devtools/factory/` | The AI factory: the referee, the guard, its config and step prompts. |

## Where knowledge lives

**One topic, one owner; everything else links to it.** Before writing anything down,
find its owner here. Every doc in `docs/` and at the root has a row or a folder here
*[enforced: `devtools/checks/check-book.py`]*. How to write each one: [`docs/CLAUDE.md`](docs/CLAUDE.md).

| Topic | Owner |
|---|---|
| **The requirements** — read-only snapshots, one file per revision; build from the highest | `requirements/` ([`read-me.txt`](requirements/read-me.txt)) |
| What the product is, its vocabulary, users and flows | `docs/system-overview.md` |
| Architecture, conventions, and **why** each rule exists | `docs/architecture.md` |
| How a screen looks and works where the requirements and wireframes do not say | `docs/ui-standard.md` |
| Feature status and phases; the latest requirements revision's changes; the task roll-up (**generated** from the plans by `devtools/checks/gen-task-rollup.py`) | `docs/project.md` |
| Requirement ↔ plan ↔ code ↔ test traceability — written only by `/fsd-audit` | `docs/Audit/spec-traceability.md` |
| **Rules decided for questions the requirements leave open** — *Resolved*, by topic | `docs/open-questions.md` |
| **A defect**: what is wrong, and what happened to it | `docs/defects.md` |
| One feature's design, and its tasks in build order with their role and status | its plan in `docs/plans/`, from `docs/plans/_template.md` — its Tasks section |
| One factory run: intent, spec, design, plan, evidence, review | its file in `docs/factory/runs/` — created by `factory start`, never by hand |
| Every factory run in one table — **generated** | `docs/factory/runs/INDEX.md` (`factory index`) |
| Closed history — fixed defects, answered questions, build logs | `docs/archive/` — **never a current rule** |
| Rules to follow while working | this file and the nested ones |
| How to run and build it | `README.md` |

**Is X decided?** Look under *Resolved* in `open-questions.md`, then the feature's
plan. A decision found only in the archive, a chat or a PR thread is not in force until
it is in one of those.

## Features

- **The requirements are the source of truth and win every conflict.**
  They are the FSD in `requirements/fsd/` (`AI-Hub - V<n>.md`); the highest `V<n>` is authoritative. V1 is
  a word-for-word Markdown copy of the client's PDF brief, kept beside it in `requirements/original/`. Where they are silent, the supporting documents are
  the best evidence. A design showing what the requirements do not specify is not a
  requirement. Where the requirements contradict themselves, ask.
- **Every feature gets a plan before it gets code**, from `docs/plans/_template.md`.
- **The plan and the code stay in sync, in the same commit.** A deviation from an
  approved plan is **discussed before it is built**, not found in the diff.
- **A question the requirements do not answer goes to `open-questions.md` with a
  recommendation** — never into a plan or a spec as a guess.
- **Every confirmed defect gets a `defects.md` row the day it is found**, and its id
  goes in the fixing commit's message. A missed requirement in a completed feature is
  a defect row, not a silent rewrite.
- **Stay inside the feature's scope.** A drive-by fix is its own commit, tracked.
- **Change behaviour, fix the doc that owns it in the same commit.** Wrong
  documentation is worse than none.

## Coding principles (every stack)

These hold in every language and framework, and the reviewer checks each change against
them: a breach is a finding, not a matter of taste. Why each exists:
[`docs/architecture.md`](docs/architecture.md#coding-principles-every-stack).

- **Less code is better code, inside the architecture's boundaries.** Build what the
  requirement asks and nothing speculative: no interface or base class without a second
  implementer in sight, no option or setting nobody asked for, no wrapper that only
  renames a library call. Delete dead code and commented-out code. Shorter code that
  crosses a layer boundary is not better: the boundary wins.
- **Neat and understandable.** Names say what a thing is or does, so the code needs few
  comments; rename unclear code rather than explain it. Short functions with one job
  each, no deep nesting (return early). No magic numbers or strings: a named constant
  beside the code that owns it.
- **Efficient by design.** No query or call inside a loop; read only the columns and
  rows needed; page long lists; do once what can be done once; never block on an async
  path. Beyond that, measure before tuning: clarity wins over a micro-optimisation
  nobody measured.
- **Reusable, without being clever.** Shared logic lives in one place, named for what it
  does. Write it twice before you abstract it; abstract it the third time. Prefer a
  maintained library over writing your own, kept behind one file.
- **The basics, always:** validate input at the boundary and trust nothing from outside;
  handle the failures you expect and let faults surface; no secrets in code or logs;
  immutable by default; one responsibility per class or module; dependencies point
  inward; every change ships with tests that prove it.

## Code rules

The rules and the why for each are in the [architecture guide](docs/architecture.md); it grows as the Boilerplate
tasks are built, and a rule is in force once it is there.

- **A module depends only on the shared kernel and other modules' `Contracts`** — never on another module's
  implementation or the host *[enforced: `ArchitectureTests`, in backend/tests/App.Tests/Architecture]*.
- **Put a type where the guide says**: another module needs it → the owner's `Contracts`; only its module →
  the implementation project; every module and no owner → `backend/src/Shared` ([where code
  lives](docs/architecture.md#2-where-code-lives)).
- **Every screen follows [`docs/ui-standard.md`](docs/ui-standard.md)** where the requirements and wireframes do not
  say; with no wireframe, the design step describes each new screen for a person to approve first.
- **Warnings are errors** in the back end (`backend/Directory.Build.props`); the front end passes `npm run lint`
  and `npm run typecheck`.

- **Never commit real secrets.** Placeholder settings are recognisably placeholders.
- **Never log** passwords, tokens, connection strings or customer-identifying data.

## The AI factory

A backlog item goes through the factory as a **run**: one branch, one run file in
`docs/factory/runs/`, and the steps `devtools/factory/factory.toml` lists. `/factory`
drives a run in your own session; `factory drive` drives it unattended.

- **The guard** (`devtools/factory/hooks/guard.py`, wired in `.claude/settings.json`)
  reads every file write and shell command before it runs and refuses what this file
  says never to do — the rules are `[guard]` in `factory.toml`. Some writes it **asks**
  about instead: the factory itself, settings, CI and git hooks.
- **Never route around a guard refusal** — not with another tool, a shell redirect, a
  script or a renamed path. A refusal means stop: fix the approach, or ask the person.
  The guard is a first layer; CI re-checks what reaches a pull request.
- **The referee** (`devtools/factory/factory.py`) checks each step **itself** and
  advances only on a pass. A session's word that a step is done is never taken.
- **Gates are a person's.** The spec gate, the design gate (High tier only) and the
  merge gate are decided by people: `factory approve` from a terminal that is not a
  Claude Code session (or the Factory app), and at the merge gate the pull request
  approved by someone other than its author — or, in a project with no remote
  (`check = "local-approval"`), a person's `factory approve merge`.
  An agent never runs `factory approve`, `unlock`, `abandon` or `open-pr` *[enforced: guard]*.
- **Tests are proven red, then locked.** A change criterion's tests must fail before the
  build; then they are locked by hash, and only a person's `factory unlock` releases
  one *[enforced: guard]*. A criterion marked *(guard)* pins behaviour that must not
  break: its tests pass before and after.
- **A run is measured by its branch's own commits** since its base
  (`git log --first-parent --no-merges <base>..HEAD`) — merges of the base branch are
  not the run's change.
- **Escalate instead of guessing.** A session that cannot make progress writes an
  `## Escalation` section in the run file saying what a person must decide, and stops.
- **A defect a run introduced** says *Introduced by run [R-nnn](factory/runs/R-nnn.md)*
  in its defect row, so `factory report` can count what escaped each run.

## Git

- **Protected branches are reached by pull request only — never push or commit to
  them** *[enforced: guard]*: `main` (`protected_branches` in `factory.toml`). This project has no remote,
  so a run reaches `main` through a person's local merge approval in the Factory app.
- **Cut work branches from the base with `--no-track`**
  (`git checkout --no-track -b <type>/<slug> <base_ref>`), so a bare `git push` cannot
  land on the base.
- **Never delete a branch, never force-push or rewrite history on a shared branch**
  *[enforced: guard]*.
- Commit messages are imperative, with **no AI or tooling attribution and no trailer
  lines**.
- No ticket tracker: a run's branch is named for its backlog item (`feature/<plan>-t<n>` for a plan's task).
- **A pull request description has a summary and a test plan.** `factory pr-body`
  writes one from a run.

## Building and testing

- **Build before committing**, in the half you changed: `dotnet build App.slnx` in `backend/`, `npm run build`
  in `frontend/` (each stack's `[stacks.commands].build` in `factory.toml`).
- **Test what the change can break, not everything**: the fast suites always, and the
  integration tests of the modules you touched. Run the full suite when the change is
  cross-cutting. Back end: `dotnet test tests/App.Tests` in `backend/`; front end: `npx vitest run`, then
  `npm run lint` and `npm run typecheck`, in `frontend/`. Cross-cutting here: the host's composition root, the
  shared kernel, the architecture test, and anything both halves read (the API's shape).
- **Report what ran** — the command or filter, and the executed, passed, failed and
  **skipped** counts. A suite that skips because a database or service is missing, or a
  filter that matches nothing, looks like a pass; neither is one.
- Every feature ships tests that prove its behaviour. **Never skip, delete or weaken a
  failing test.**
