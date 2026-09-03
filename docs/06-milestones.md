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

## M1 — Foundation & Assets — IN PROGRESS

Solution structure, PostgreSQL, EF Core migrations, Identity + RBAC, audit
foundation, Assets + Locations (CRUD + hierarchy), frontend app shell +
design system + Light/Dark/System, Docker Compose (API + Postgres), CI
(build + test on push), baseline unit/integration tests.

Progress so far:
- `409e324`/`42a184b`: backend solution scaffold (IdentityAccess + Assets
  modules, cookie/CSRF auth, database-enforced site-boundness); frontend
  app shell, design tokens, no-flash Light/Dark/System theming.
- `8058df4`: CI (GitHub Actions — backend build+test, frontend lint+
  typecheck+build), verified green.
- `aa72a13`: frontend baseline tests (theme system, app shell — 15 tests),
  wired into CI.
- `fed913e`: backend audit trail (append-only, same-transaction writes),
  Asset/Location CRUD, and the first real RBAC enforcement
  (`IPermissionEvaluator`, live DB state, no cached claims) — Codex Backend
  hit its weekly quota (2% left) mid-M1, so this slice was implemented by
  the orchestrator directly per the fallback protocol. PostgreSQL
  integration tests (Testcontainers) prove cross-site denial, the
  `site_id`-immutable trigger firing, and audit-write atomicity — verified
  independently, not just self-reported, and green in CI on GitHub's
  runners too.
- `0982576`: Asset List + Asset Detail frontend pages per docs/04, built
  against mock data pending backend wiring.

Still open before M1 can be marked `PASS`:
- Frontend Asset pages wired to the real API (currently mock data) — in
  progress.
- Codex QA smoke pass on the RBAC/audit/Assets slice — in progress (Codex
  QA is also low on weekly quota, so this pass is deliberately narrow:
  BLOCKER-only, two files, re-running the existing test suite rather than
  open-ended exploration).
- Real rendered visual/responsive verification (both themes, mobile
  viewport) — currently verified by code inspection only (semantic tokens,
  responsive class patterns) because headless-Chromium screenshotting needs
  a `sudo apt-get` the agents can't run non-interactively and it wasn't
  judged worth interrupting the user for. Tracked gap, not silently
  dropped — revisit at the latest during M6's responsive/visual QA pass.
- A real concurrent-write invariant test (e.g. two callers racing an
  optimistic-concurrency edit) isn't in scope yet — the concurrency-critical
  invariants that exist so far (site immutability, audit atomicity) are
  tested; the classic "two people claim the same X" race test belongs to
  the Work Order claim flow in M2, not Assets.

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
