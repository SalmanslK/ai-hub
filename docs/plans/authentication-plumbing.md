# Authentication plumbing — Design

**Status:** `Not started` · **Owner:** — · **FSD:** §7 · **Checked against:** FSD V1 · **Depends on:** Boilerplate and architecture

How a person signs in to AI-Hub and which roles every protected feature checks: a new
Accounts module that holds accounts, teams and roles, a session cookie on AI-Hub's own
origin, every endpoint closed unless it says otherwise, and the front end's sign-in screen
and account in the top bar. No FSD section owns it; §7 constrains it. It rests on two
open questions, [Q4](../open-questions.md#q4--may-people-sign-in-to-ai-hub-with-an-email-and-password-it-keeps-itself-for-now)
(how people sign in) and [Q5](../open-questions.md#q5--should-ai-hub-have-three-roles-employee-team-leader-and-administrator)
(which roles exist); it is built on their recommendations and changes if they are answered
otherwise.

---

## 1. What the FSD says

- **§7 Privacy & Governance** — "Privacy should be a core design principle—not an
  afterthought. Your solution should demonstrate: Employee consent and opt-in · **Role-based
  access control** · Sensitive data protection · Responsible AI practices · Transparency into
  what data is collected and why." Role-based access control needs a known person behind every
  request, and roles to check; sensitive data protection covers the passwords and the session.
- The Problem Statement's challenge asks for employees "to securely connect their AI
  applications with explicit consent", and names "employees and leadership" as the audiences,
  with measurement done "while protecting employee privacy". §6's "Team-level analytics" is
  the reason a person belongs to a team. Those sections' screens are their own features'.

V1 names no sign-in method, account source, roles or permissions
([system-overview, Users and roles](../system-overview.md#users-and-roles)).

## 2. The decision this rests on

**[PROPOSED] (Q4) AI-Hub keeps its own accounts, on ASP.NET Core Identity, with a session
cookie.** An account is an email and a password stored only as Identity's one-way hash; signing
in sets an `HttpOnly`, `SameSite=Strict` cookie on AI-Hub's own origin, which the front end
already shares with `/api` ([architecture §6](../architecture.md#6-cross-cutting-decisions)).

- *Rejected: company single sign-on (OpenID Connect) now.* No provider is named and the demo
  runs on one machine with no cloud (Q2); it is presented as a future step (§8), and the sign-in
  lives behind the Accounts module's endpoints so it can be swapped without touching a feature.
- *Rejected: bearer tokens (JWT) in the browser.* A token the page's script can read is exposed
  to any script injected into it; an `HttpOnly` cookie is not, and same origin needs no token
  hand-off. *Decision made here.*
- *Rejected: an identity library or server of our own.* Identity is first-party and maintained.

**[PROPOSED] (Q5) Three roles — Employee, Team leader, Administrator — and every endpoint is
closed by default.** The host's fallback authorization policy requires a signed-in person, so an
endpoint a feature forgets to protect is closed, not open; an endpoint open to anyone says so
with `AllowAnonymous`. *Decision made here.*

## 3. Model

- **Account** — one per person: email (unique, case-insensitive), display name, password hash,
  one team, and one or more roles. Every account holds **Employee** [PROPOSED] (Q5).
- **Team** — a name; an account belongs to exactly one. A Team leader leads the team their
  account belongs to [PROPOSED] (Q5).
- **Role** — a fixed set, `Employee`, `TeamLeader`, `Administrator`, named once as constants in
  `App.Accounts.Contracts`; not data a person edits. *Why:* a feature's check is code, so a role
  that is only data could not be checked [PROPOSED] (Q5).
- **The signed-in person** — what a feature reads: account id, team id, roles. Read from the
  session's claims, never from the request body.

Invariants: an email is unique (the database, §7); an account has the Employee role and a team
(the seeding and Identity's validators); a password never leaves the Accounts module and never
reaches a log ([architecture §3](../architecture.md#sensitive-data-never-reaches-a-log)).

## 4. Flows

1. **Start** — the host applies the `accounts` schema's migrations (already built,
   [§5 Data](../architecture.md#5-data)), then creates the demo accounts listed in settings if
   they do not exist: one per role, their passwords from the environment, never committed
   (Q4, [Settings](../architecture.md#settings-are-validated-at-start-up-and-committed-settings-hold-no-secrets)).
   It never changes an account that exists.
2. **Sign in** — the person enters email and password; `POST /api/auth/sign-in` checks them. A
   wrong password and an unknown email return the same `401`, so the answer does not reveal
   which emails exist. Five failures in a row lock the account for fifteen minutes (Identity's
   lockout). *Decision made here.* On success the cookie is set and the screen goes to where the
   person was headed.
3. **Each request** — the cookie authenticates the request; the fallback policy rejects one with
   no session with `401`, and a role policy rejects a person without the role with `403`, both as
   problem details ([§4 Errors](../architecture.md#4-errors)) — never a redirect, since the
   caller is the front end. The session slides: it ends after eight hours with no request.
   *Decision made here.*
4. **The front end** — on load it asks `GET /api/auth/me`; `401` sends the person to the sign-in
   screen with where they were headed, `200` shows their name in the top bar. A screen or action
   a person's roles do not allow is hidden or shows the no-permission state
   ([ui-standard §3](../ui-standard.md#3-every-screens-states)).
5. **Sign out** — `POST /api/auth/sign-out` ends the session; the front end returns to sign-in.

## 5. Where each piece lives

- **A new module, Accounts** — `backend/src/Modules/Accounts/` with both projects, added to
  `App.slnx` and `DependencyRule.Modules`
  ([architecture §2](../architecture.md#2-where-code-lives)). `App.Accounts.Contracts` holds
  the role names, the policy names and the signed-in person's reader every other module uses;
  `App.Accounts` holds Identity, its store and the endpoints. *Why not `Shared`:* roles and
  accounts have one owner.
- **The host** — registers authentication and authorization and the fallback policy; marks
  `GET /health`, `GET /api/openapi.json` and the sign-in endpoint anonymous.
- **The front end** — the sign-in screen in `frontend/src/features/account/`; the session (who
  is signed in, sign out) in `frontend/src/api/` beside the client, since every feature reads
  it; the account and sign-out in the shell's top bar (`frontend/src/shell/`).
- **Already built, used rather than recreated:** the redacting logger (`authorization`,
  `token`, `password` are already redacted), problem-details errors, the schema-per-module data
  access and migrations at start-up (Q1), settings validated at start-up, the typed API client.
- **Obligations:** each task ships the tests that prove it — every endpoint's `401` and `403`
  among them; task 2 records the authorization rule in `architecture.md` (§3, and §9's mandatory
  tests) and regenerates the front end's `schema.d.ts`; task 3 fills `ui-standard.md` §7's
  account in the top bar; `README.md` names the demo accounts' settings.

## 6. Public surface

| Surface | Who may use it | Returns |
|---|---|---|
| `POST /api/auth/sign-in` — email, password | Anyone | `204` and the session cookie; `400` problem details when a field is missing or not an email; `401` problem details "The email or password is not correct." for a wrong password, an unknown email or a locked account |
| `POST /api/auth/sign-out` | A signed-in person | `204`, the cookie cleared; `401` with no session |
| `GET /api/auth/me` | A signed-in person | `200` with id, display name, email, team and roles; `401` with no session |
| Any other `/api/*` endpoint | A signed-in person, and the roles its feature's plan names | `401` problem details with no session; `403` problem details without the role |
| Sign-in screen, `/sign-in` | Anyone; a signed-in person is sent to the home screen | Email and password fields and one action, "Sign in"; the error in plain words beside the form. No wireframe: how it works is [ui-standard §4 Forms](../ui-standard.md#4-forms) |
| The top bar's account | A signed-in person | Their display name and "Sign out" ([ui-standard §7](../ui-standard.md#7-todays-conventions)) |
| No-permission state | A signed-in person without the role a screen needs | A message that they do not have access and a link home ([ui-standard §3](../ui-standard.md#3-every-screens-states)) |

Managing accounts, teams and roles from a screen is not this feature's: Administrators' screens
come with Privacy & Governance. Until then the accounts are the demo accounts of flow 1.

## 7. Data and migrations

The `accounts` schema, owned by the Accounts module, holds Identity's tables and the teams. The
database enforces what the code cannot: an email unique across accounts (Identity's normalised
email, unique index) and an account's team existing (foreign key). Passwords are stored only as
Identity's hash; the database never holds one in clear.

## 8. Open questions

- [Q4](../open-questions.md#q4--may-people-sign-in-to-ai-hub-with-an-email-and-password-it-keeps-itself-for-now)
  — sign-in with accounts AI-Hub keeps itself, single sign-on later. Open; §2 and tasks 1–3
  build on its recommendation.
- [Q5](../open-questions.md#q5--should-ai-hub-have-three-roles-employee-team-leader-and-administrator)
  — the three roles and what each may see. Open; §3 and tasks 1, 2 and 4 build on its
  recommendation.

## 9. Tasks

| # | Task | FSD | Role | Status | Jira | PR | Merged |
|---|---|---|---|---|---|---|---|
| 1 | Back end: the Accounts module — accounts, teams and the three roles on ASP.NET Core Identity in the `accounts` schema with its migration; the demo accounts, one per role, created at start-up from settings with passwords from the environment (Q4, Q5) | §7 | BE | Not started | — | — | — |
| 2 | Back end: sign-in — cookie authentication, `POST /api/auth/sign-in`, `POST /api/auth/sign-out`, `GET /api/auth/me`, lockout; the fallback policy closing every endpoint, role policies and the signed-in person's reader in `App.Accounts.Contracts`, `401`/`403` as problem details; record the rule in `architecture.md`, regenerate `schema.d.ts` (Q4, Q5) | §7 | BE | Not started | — | — | — |
| 3 | Front end: the session — the sign-in screen, sending a person with no session to it and back, the account and "Sign out" in the top bar (Q4) | §7 | FE | Not started | — | — | — |
| 4 | Front end: role checks — a screen or action shown only to the roles it names, and the no-permission state for the rest (Q5) | §7 | FE | Not started | — | — | — |
