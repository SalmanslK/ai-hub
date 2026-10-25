# Open questions — what the requirements do not settle

**Status:** `Live` · **Owner of the record:** whoever raises the question

Every question the requirements leave open. **Open ones are in full; each answered
one is a single line under [Resolved](#resolved), grouped by topic, stating the rule
now in force** — the question as asked and the answer in full move to
`archive/questions-resolved.md`. How to raise and close one:
[`docs/CLAUDE.md`](CLAUDE.md#questions).

**Add to it rather than resolving a question in a chat thread**, and record the answer
and date against the question so the reasoning survives. A factory run that meets a
question the requirements do not answer escalates; the question lands here, never in
the run's spec as a guess.

Sources: the FSD, `requirements/fsd/AI-Hub - V1.md` — the highest V<n> in `requirements/fsd/` is authoritative.

## How to use it

1. **Take the next `Q<n>`** — check the open branches as well as the base; ids are never
   reused.
2. **Add a summary row** under [Still open](#still-open) and **the question in full**
   under [The open questions in full](#the-open-questions-in-full).
3. **Write it in plain words, one closed question, no section numbers** — and **always
   with a recommendation**. Unless the **Answer** line says otherwise, the
   recommendation is what gets built, so disagreeing with one is as useful as answering
   it, and silence is not neutral.
4. **When it is answered:** move the full section to the archive and add one line under
   [Resolved](#resolved), in its topic, stating the rule now in force. Keep the heading
   text exactly, so its anchor still works.

**Status key:** `OPEN` · `ANSWERED` (a rule is in force — see [Resolved](#resolved)) ·
`SUPERSEDED` (overtaken by another question). Nothing else.

### Which document wins

**The requirements are the source of truth and override any conflict.** Supporting
documents assist; they do not override.

| Situation | What we do |
|---|---|
| A supporting document contradicts the requirements | **Build the requirements.** Log the disagreement so the other document gets corrected |
| A design shows something the requirements do not specify | **Do not build it.** Ask for the requirements to be amended |
| The requirements are *silent* | Use the supporting documents; they are the best evidence there is |
| The requirements contradict **themselves** | Precedence does not help. Ask |

---

## Still open

| | Area | Question | What we suggest | Blocks |
|---|---|---|---|---|
| [Q2](#q2--may-ai-hub-run-as-containers-on-one-server-with-no-cloud-provider-chosen-yet) | Deployment and environments | May AI-Hub run as containers on one server, with no cloud provider chosen yet? | Yes: one image (API serving the built front end) plus PostgreSQL, started by Docker Compose; two environments, local and one demo | The Deployment and environments plan |
| [Q3](#q3--may-a-person-deploy-to-the-demo-server-by-running-one-script-after-a-merge-with-every-check-in-one-script-of-its-own) | Deployment and environments | May a person deploy to the demo server by running one script after a merge, with every check in one script of its own? | Yes: one checks script run before every merge, one deploy script run after it; a hosted pipeline calls the same scripts once the repository has a server | The Deployment and environments plan's checks and deploy tasks |
| [Q4](#q4--may-people-sign-in-to-ai-hub-with-an-email-and-password-it-keeps-itself-for-now) | Authentication plumbing | May people sign in to AI-Hub with an email and password it keeps itself, for now? | Yes: accounts and passwords held by AI-Hub, a few demo accounts created on start-up; company single sign-on described as a future step | The Authentication plumbing plan |
| [Q5](#q5--should-ai-hub-have-three-roles-employee-team-leader-and-administrator) | Authentication plumbing | Should AI-Hub have three roles: employee, team leader and administrator? | Yes: employees see their own data, team leaders see their team only in aggregate, administrators run the platform | The Authentication plumbing plan; every protected feature's permissions |

### The open questions in full

Format:

```
#### Q<n> · <the question, in plain words>
**Status:** OPEN · **Area:** <module or requirement> · **Blocks:** <what cannot be built until it is answered>

<the context: what the requirements say, and why it is not enough>

**Recommendation:** <what we would build, and why>

**Answer:** —
```

---

## Superseded

Questions overtaken by another; none carries a rule of its own.

---

## Resolved

**The rule now in force for every answered question, by topic.** One line each —
enough to build from. The question as asked, the answer and the reasoning are in
`archive/questions-resolved.md`.

Format:

```
#### Q<n> · <the question's heading, exactly>

`ANSWERED` · <date> · <who answered> — <the rule now in force>. [Full record](archive/questions-resolved.md#<anchor>)
```

### Boilerplate and architecture

#### Q1 · May AI-Hub keep its data in one PostgreSQL database?

`ANSWERED` · 2026-10-15 · owner — AI-Hub keeps its data in one PostgreSQL database, with a separate schema for each module; a module's migrations touch only its schema. [Full record](archive/questions-resolved.md#q1--may-ai-hub-keep-its-data-in-one-postgresql-database)

### Deployment and environments

#### Q2 · May AI-Hub run as containers on one server, with no cloud provider chosen yet?

`ANSWERED` · 2026-10-19 · owner — Yes. Run AI-Hub in Docker on one machine, with its database beside it. No cloud for now; we will describe the cloud as a future step. [Full record](archive/questions-resolved.md#q2--may-ai-hub-run-as-containers-on-one-server-with-no-cloud-provider-chosen-yet)

#### Q3 · May a person deploy to the demo server by running one script after a merge, with every check in one script of its own?

`ANSWERED` · 2026-10-19 · owner — Yes. One script runs every check before a merge. A second script starts the new version after the merge and goes back to the old version if it fails. [Full record](archive/questions-resolved.md#q3--may-a-person-deploy-to-the-demo-server-by-running-one-script-after-a-merge-with-every-check-in-one-script-of-its-own)

### Authentication plumbing

#### Q4 · May people sign in to AI-Hub with an email and password it keeps itself, for now?

`ANSWERED` · 2026-10-20 · owner — Yes. For now AI-Hub keeps its own accounts: an email and a password, stored safely as a hash. A few demo accounts, one per role, are created on start-up. Signing in with the company's single sign-on comes later. [Full record](archive/questions-resolved.md#q4--may-people-sign-in-to-ai-hub-with-an-email-and-password-it-keeps-itself-for-now)

#### Q5 · Should AI-Hub have three roles: employee, team leader and administrator?

`ANSWERED` · 2026-10-20 · owner — Yes, three roles, and a person can have more than one. Employee: everyone; sees only their own data, insights and the Knowledge Hub. Team leader: also sees their own team's totals, never one person's activity. Administrator: also manages accounts, roles and settings, and sees company-wide totals. No one sees another person's raw usage. [Full record](archive/questions-resolved.md#q5--should-ai-hub-have-three-roles-employee-team-leader-and-administrator)
