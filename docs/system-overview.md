# AI-Hub — System overview

**What the product is, the words it uses, who uses it, and what they do with it.** It
describes the product, not the code: how it is built is the
[architecture guide](architecture.md), and where each feature has got to is
[`project.md`](project.md). The requirements win every conflict with this page — see
[`requirements/`](../requirements/read-me.txt). Written from FSD V1 (§ numbers below are its sections);
where V1 is silent, this page says so rather than deciding.

---

## What it is

AI-Hub is a centralised **Enterprise AI Intelligence Platform**. Employees use many AI tools on their own —
ChatGPT, Claude, Gemini, Cursor, GitHub Copilot and others — so what they learn stays scattered, and the
organisation cannot see which tools are used, how well, or what knowledge and skills they reveal (Problem
Statement). AI-Hub discovers the AI tools in use, lets employees connect them **with explicit consent**,
captures usage events, and turns them into employee insights, explainable recommendations, a reusable knowledge
hub and adoption analytics (Your Challenge, §2).

It is a **working prototype (MVP)**: it must connect to at least one real AI tool and capture meaningful usage
events; further integrations may be simulated (§1). Privacy is a core design principle, not an afterthought
(§7). V1 names no other limit on scope. §8 (Demo & Documentation) is what the team presents, not a feature of
the product.

## Vocabulary

The words the requirements use, and what each means here. **Use these words in code,
plans and docs**; a synonym is a second name for the same thing, and drifts.

| Term | Means | Not to be confused with |
|---|---|---|
| **AI tool** | An AI application an employee uses for work: ChatGPT, Claude, Gemini, Cursor, GitHub Copilot and others (Problem Statement) | AI-Hub's own intelligence features |
| **Discovery** | Finding which AI tools are used across the workforce (Your Challenge, §2); how it works is not yet decided | Connection |
| **Connection** | An employee securely linking one of their AI tools to AI-Hub, with explicit consent (Your Challenge) | Discovery |
| **Consent** | An employee's explicit opt-in to having an AI tool's usage collected (§7) | Role-based access, which governs who may *see* data |
| **Usage event** | One meaningful record of an employee's use of a connected AI tool, captured by AI-Hub (§1) | An insight, which is derived from many events |
| **Employee insight** | What AI-Hub infers from usage patterns: skills and technical expertise, persona or role, preferred AI tools, working style or productivity patterns (§3) | A role in AI-Hub's access control |
| **Persona** | The employee's inferred role or kind of work (§3) | An access-control role |
| **Recommendation** | An explainable suggestion backed by data: the most appropriate AI tool for a task, relevant prompts or workflows, or best practices (§4) | A knowledge item itself |
| **Knowledge Hub** | The central, searchable and tagged repository of reusable knowledge: prompt library, workflow templates, AI playbooks, best practices (§5) | The analytics dashboard |
| **AI adoption** | How widely and how effectively AI is used across the organisation, measured responsibly (§6, §7) | Tool popularity, one of its metrics |

## Users and roles

V1 asks for **role-based access control** (§7) but names no roles, and no permissions per role. The people it
names are below; which access-control roles exist, and what each may see, is **not decided** — it is a question
for the Authentication plumbing feature's plan, never a guess.

| Role | Who they are | What they can do |
|---|---|---|
| Employee | Anyone who uses AI tools at work (Problem Statement) | Connect their AI tools with consent, see what is collected and why (§7), get recommendations (§4), discover and reuse knowledge in the Knowledge Hub (§5) |
| Leadership | The people who decide on AI tools and adoption (Your Challenge) | Read organisation-wide and team-level analytics (§6) |
| Technical and business users | The dashboard's audience: it must be intuitive for both (§6) | A description of the audience, not a separate role |

## Main flows

Information flows **Discovery → Connection → Event Capture → Data Processing → Intelligence → Recommendations →
Dashboard** (§2). The features below are the rows of [`project.md`](project.md); none is built yet.

### Connect an AI tool and capture usage

An employee sees which AI tools are in use, chooses one, reads what will be collected and why, and opts in. AI-Hub
then captures that tool's usage events. Crosses: Privacy & Governance, AI tool discovery, AI tool connection and
usage capture (§1, §7).

### Turn usage into employee insight

Captured events are processed into each employee's skills, persona, preferred tools and working patterns, with
how each was generated shown rather than hard-coded (§3). Crosses: AI tool connection and usage capture, Employee
Intelligence Layer.

### Ask what to use

An employee describes a task and gets the most appropriate AI tool, relevant prompts or workflows, and best
practices, each with the data behind it (§4), drawing on the Knowledge Hub (§5). Crosses: Recommendation Engine,
Knowledge Hub.

### Read adoption across the organisation

Leadership and teams see adoption, tool popularity, usage trends, productivity, skills used for agentic
development, team-level analytics, AI effectiveness and recommendation insights (§6). Crosses: Analytics
Dashboard, over everything above.

## Systems it talks to

- **AI tools:** at least one real one, connected for real; the others may be simulated (§1). V1 names ChatGPT,
  Claude, Gemini, Cursor and GitHub Copilot as examples; which one is connected first is **not decided**.
- Nothing else is named in V1.
