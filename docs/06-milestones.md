# Milestones & Definition of Done

Status legend: `PENDING` not started · `IN PROGRESS` · `PASS` verified ·
`BLOCKED` waiting on something.

## M0 — Discovery & Architecture — **M0 — APPROVED**

Reviewed by Codex Backend (architecture critique: no blockers, two cleanup
notes applied) and Codex QA (adversarial pass: 6 BLOCKER / 7 IMPORTANT / 4
OPTIONAL findings, all resolved inline in `01-domain-and-workflows.md` and
`02-security-and-invariants.md` — see `docs/qa/m0-adversarial-review.md`
for the original findings and `03-architecture-decisions.md`'s intro note
for how to trace each one to its resolution). Proceeding to M1.

Deliverables (docs/ in this repo):
- Product vision, personas, scope/out-of-scope (`00-product-vision.md`)
- Domain workflows, Work Order lifecycle, preventive lifecycle, KPI
  definitions (`01-domain-and-workflows.md`)
- Roles & permission matrix, threat model, business invariants &
  concurrency/idempotency strategy (`02-security-and-invariants.md`)
- Architecture decisions: DB strategy, job strategy, QR strategy, attachment
  decision, observability/new-tech decisions (`03-architecture-decisions.md`)
- Frontend IA, visual direction, Light/Dark strategy, mobile strategy
  (`04-frontend-ia.md`)
- This milestones/DoD doc (`06-milestones.md`)

DoD: all docs above exist, reviewed by Codex Backend (backend/architecture
critique) and Codex QA (adversarial pass on permission matrix / threat model
/ invariants) with no open BLOCKER, then marked `M0 — APPROVED`.

## M1 — Foundation & Assets

Solution structure, PostgreSQL, EF Core migrations, Identity + RBAC, audit
foundation, Assets + Locations (CRUD + hierarchy), frontend app shell +
design system + Light/Dark/System, Docker Compose (API + Postgres), CI
(build + test on push), baseline unit/integration tests.

DoD: `docker compose up` boots API + DB + runs migrations; CI green on a
fresh clone; login → asset list → asset detail works in both themes on
desktop and mobile viewport; Codex QA smoke pass on auth/RBAC with no
BLOCKER.

## M2 — Requests & Work Orders

Maintenance requests, Work Order lifecycle (creation, assignment, state
transitions, priority), permission enforcement per role, history/audit,
concurrency protection on claim/assign/complete, frontend WO list/board/detail
flows.

DoD: state machine cannot be forced into an invalid transition via API;
concurrent claim of the same WO resolves to exactly one winner (proven by a
real concurrent test, not asserted); Codex QA adversarial pass (IDOR,
authz bypass, invalid transitions) with no BLOCKER.

## M3 — Preventive Maintenance

Maintenance plans, recurrence rules, background scheduler, automatic
Preventive Work Order generation, idempotency against duplicate generation
(single job firing twice, two scheduler instances), maintenance calendar UI.

DoD: a concurrency/idempotency test proves a plan cannot generate two work
orders for the same due occurrence even under simulated duplicate
trigger/multiple-instance conditions; Codex QA pass with no BLOCKER.

## M4 — Maintenance Execution

Technician workflow, checklist execution, downtime capture, parts/costs
(lean scope), attachments (if approved in M0), QR-driven navigation
(Asset → Work Order → Start → Checklist → Notes/Evidence → Complete), mobile
UX, attachment/QR security enforcement.

DoD: QR scan never grants authorization beyond what the technician's role
already allows (proven by a negative test); attachment upload rejects
path traversal / disallowed types / oversized files; full mobile flow usable
on a real small-viewport browser session; Codex QA pass with no BLOCKER.

## M5 — Reporting & Operations

MTBF, MTTR, availability, downtime, backlog, cost, preventive-vs-corrective
reporting, operational dashboard, SignalR for live updates (if approved in
M0).

DoD: every KPI formula is documented and matches its cited industry
definition; dashboard numbers reconcile against raw work-order/downtime data
in a test; Codex QA pass with no BLOCKER.

## M6 — Production Readiness

Adversarial QA sweep, security hardening review, concurrency re-verification,
observability check, performance sanity, responsive/accessibility QA,
frontend visual polish, Docker + CI finalization, deploy (Vercel + Render +
Neon), production smoke test, README + architecture docs + Notion page,
interview-ready explanations.

DoD: independent QA full pass with all BLOCKER/IMPORTANT resolved; backend
build + unit tests + PostgreSQL/Testcontainers integration tests all green;
frontend typecheck/lint/tests/build all green; Docker images build; CI green
on `main`; real production deployment reachable; production smoke test
exercises a real authenticated flow against the deployed app (not a fake
check); documentation published.

## Closure

When M0–M6 are all `PASS`, produce a closure report distinguishing PASS /
PENDING / BLOCKED / NOT EXECUTED per check — no invented validation. Only
then: `PROJECT COMPLETE`, `DEPLOYED`, `PORTFOLIO READY`, `FROZEN`.
