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

## M1 — Foundation & Assets — **PASS**

Solution structure, PostgreSQL, EF Core migrations, Identity + RBAC, audit
foundation, Assets + Locations (CRUD + hierarchy), frontend app shell +
design system + Light/Dark/System, Docker Compose (API + Postgres), CI
(build + test on push), baseline unit/integration tests.

DoD check: `docker compose up` boots API+DB and runs migrations — PASS,
verified repeatedly. CI green on a fresh checkout — PASS
(github.com/guilhermedev66/CMMS/actions). Login → asset list → asset detail
works — PASS, verified live against the real running backend, not mocks.
Codex QA smoke pass on auth/RBAC with no open BLOCKER — PASS (1 BLOCKER
found on a cross-site `ParentAssetId` gap, fixed with a regression test,
commit `a8f4cd2`). One item is intentionally **not** fully verified: real
rendered screenshots confirming both themes/mobile viewport look right —
verified by code inspection (semantic tokens only, consistent responsive
class patterns) rather than a rendered screenshot, because headless-
Chromium's system deps need an interactive `sudo` the agents can't supply
non-interactively, and it wasn't judged worth interrupting the user for a
nice-to-have. Tracked explicitly, revisit at the latest in M6.

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
- `a8f4cd2`: fixed a Codex QA BLOCKER (cross-site `ParentAssetId` fell
  through to an unhandled DB constraint exception instead of a clean
  validation error) with a regression test.
- `7ee1409`: Asset List/Detail wired to the real backend API — login page,
  cookie-session `AuthContext`/`ProtectedRoute`, CSRF-aware API client.
  Verified live against the real running Docker Compose backend.

Deliberately deferred (not blockers, tracked): real rendered visual QA
across themes/viewport (see DoD note above); the classic "two callers
claim the same resource" concurrency race test, which belongs to the Work
Order claim flow in M2 — Assets' own concurrency-critical invariants (site
immutability, audit atomicity) are already tested.

DoD: `docker compose up` boots API + DB + runs migrations; CI green on a
fresh clone; login → asset list → asset detail works in both themes on
desktop and mobile viewport; Codex QA smoke pass on auth/RBAC with no
BLOCKER.

## M2 — Requests & Work Orders — **PASS**

Maintenance requests, Work Order lifecycle (creation, assignment, state
transitions, priority), permission enforcement per role, history/audit,
concurrency protection on claim/assign/complete, frontend WO list/board/detail
flows.

DoD check: state machine cannot be forced into an invalid transition via API
— PASS, proven by `WorkOrdersConcurrencyTests` (Complete-before-Start,
double-Publish both return `409 Conflict`, never `500`). Concurrent claim of
the same WO resolves to exactly one winner — PASS, proven by a genuinely
concurrent test (`Two_technicians_racing_to_self_claim_the_same_open_work_order_resolve_to_exactly_one_winner`,
two real HTTP requests via `Task.WhenAll` against the real running host and a
real PostgreSQL instance, not a single-threaded simulation): exactly one
`200`, one `409`, and the persisted row confirms exactly one assignee.
Adversarial pass (IDOR, authz bypass, invalid transitions) — PASS, no
BLOCKER: `MaintenanceRequestsAndWorkOrdersRbacTests` covers cross-site
Work Order read/self-claim denial (404, not 403 — existence never
confirmed), a same-site non-assignee technician blocked from Start/Complete
on someone else's order, `requests.cancel.own` correctly having no "any"
counterpart (a second Requester at the same site cannot cancel another's New
request), double-convert/reject-after-convert both returning `409` (not a
duplicate Work Order), and cross-site Request read denial. Codex QA was
unavailable this session (per the fallback protocol, same as M1's
`fed913e`) — these 5 adversarial tests were written and run by the
orchestrator directly; independent re-review is still open, tracked below.

Scope cuts, deliberate and documented inline (see
`src/Cmms.Api/WorkOrdersEndpoints.cs`'s doc comment and
`src/Modules/WorkManagement/Domain/WorkOrderStatus.cs`): no `OnHold` state,
no Planner-driven Assign/Reassign/Unassign/Reschedule — only self-claim
moves `Open -> Scheduled` in this slice, which is what the flagship
concurrency test actually needs. `Priority` (P1-P4) was added to both
`WorkOrder` and `MaintenanceRequest` — not in docs/01's original scope but
formalizing what docs/02's permission catalog (`workorders.prioritize`) and
docs/04's frontend IA already assumed; no dedicated "reprioritize" endpoint
yet (set once at creation/conversion). Frontend ships the Grid list view
only (docs/04's "default for planners" mode) with inline guarded actions;
the Kanban board (drag-and-drop) is deferred, not required by this
milestone's DoD. `GET /auth/me` was extended with `siteMemberships` +
`isAdmin` — needed for the frontend to know which site to submit a
Request/Work Order against, since no `sites.manage`/`users.manage` endpoint
exists yet to look this up any other way.

Progress:
- Backend: `MaintenanceRequests` + `WorkManagement` modules (domain,
  EF Core + migrations, site-immutability trigger, cross-schema FK to
  `identity_access.sites`), wired into `Cmms.Api`/`Cmms.sln`/Docker build.
  Endpoints: Request create/list/get/convert/reject/cancel; Work Order
  create/list/get/publish/self-claim/start/complete/close/reopen/cancel.
  Convert-to-Work-Order and the terminal Request transitions are atomic
  conditional `UPDATE ... WHERE status = 'New'` inside a
  `SharedTransactionScope` with the audit write, per docs/01 §
  "Resolves QA finding B-04(1)" and docs/02's concurrency protocol.
- Frontend: real API clients (`api/requests.ts`, `api/workOrders.ts`)
  replacing the M2-interim mocks; Requests list + create dialog + Convert/
  Reject/Cancel actions; Work Orders Grid + create dialog + detail page,
  `StatusTransitionMenu` driving every guarded action from the real
  backend-supported set (no client-side transition invented that the
  server doesn't also enforce).
- Verified: `dotnet build`/`dotnet test` on `Cmms.sln` green (14/14
  integration tests, real PostgreSQL via Testcontainers); frontend
  `lint`/`build` (tsc -b + vite build)/`test` (16/16) green; `docker
  compose up` boots API + DB, migrations apply cleanly from a reset
  volume, `/health` reports healthy.
- **Not** verified: a real rendered browser screenshot of the new pages —
  same blocker as M1 (headless Chromium's system deps need an interactive
  `sudo` unavailable non-interactively) and, additionally here, there is no
  user-provisioning endpoint yet to log in as anything but the
  site-membership-less bootstrap Admin, so even a successful screenshot
  couldn't exercise the create-Request/Work-Order flow live. The RBAC
  integration tests are the actual verification of that flow (real HTTP,
  real Postgres, real password hashing via `UserManager`) — a stronger
  guarantee of correctness than a manual click-through would have been,
  even though it isn't a rendered screenshot. Revisit the visual pass at
  the latest in M6.

Pending: independent Codex QA re-review of this slice when available;
Kanban board; OnHold/Assign/Reassign/Unassign/Reschedule (tracked as a
follow-up, not silently dropped).

DoD (original): state machine cannot be forced into an invalid transition
via API; concurrent claim of the same WO resolves to exactly one winner
(proven by a real concurrent test, not asserted); Codex QA adversarial pass
(IDOR, authz bypass, invalid transitions) with no BLOCKER.

## M3 — Preventive Maintenance — **PASS**

Maintenance plans, recurrence rules, background scheduler, automatic
Preventive Work Order generation, idempotency against duplicate generation
(single job firing twice, two scheduler instances), maintenance calendar UI.

DoD check: a concurrency/idempotency test proves a plan cannot generate two
work orders for the same due occurrence even under simulated duplicate
trigger/multiple-instance conditions — PASS, proven by
`MaintenancePlanGenerationTests.Two_concurrent_sweeps_on_the_same_due_plan_generate_exactly_one_occurrence_and_work_order`:
two calls to `IMaintenancePlanGenerationRunner.RunSweepAsync()` launched via
`Task.WhenAll` from two separate DI scopes (simulating two scheduler
instances/overlapping ticks) against the real running host + real
PostgreSQL, asserting exactly one `MaintenancePlanOccurrence` row and the
plan's `ActiveOccurrenceId` pointing at it; a third sweep afterward still
generates nothing (SuppressIfOpen). Also covered: a Paused plan is never
swept even though due, and Floating recurrence correctly recomputes
`NextDueAtUtc` from the Work Order's actual completion timestamp (not
generation time) while clearing the active pointer. Independent Codex QA
pass — unavailable this session (fallback protocol, same as M1/M2); tracked
below as pending, same as M2's.

Design, per docs/01's "Resolves QA finding B-04(2)" and docs/02's
concurrency table row "Two scheduler ticks/instances": generation is
two-phase — a short `SELECT ... FOR UPDATE SKIP LOCKED` batch-claim (work
distribution only, releases its lock immediately) picks candidate due plan
ids, then a per-plan transaction re-locks that one row with a blocking
`SELECT ... FOR UPDATE`, re-validates `Active` + no already-open occurrence
under the lock, and only then inserts the occurrence + creates the Work
Order + advances the plan, all atomically. The occurrence's unique
`(plan_id, scheduled_for)` index is the documented final safety net "even
if the lock protocol is ever bypassed." A Work Order reaching Completed/
Closed/Cancelled clears the plan's active pointer in the *same transaction*
as that Work Order's own state change (wired into
`WorkOrdersEndpoints.TransitionAsync`, not a separate best-effort step);
Floating plans additionally recompute `NextDueAtUtc` from the real
completion time at that point, not at generation time.

Scope cuts, documented inline (`MaintenancePlan`'s doc comments): calendar
recurrence is day-interval-based (`IntervalDays` + `NextDueAtUtc`), not a
full RRULE/cron grammar — Fixed advances immediately at generation, Floating
only advances at actual completion, matching docs/01's two definitions
without building a general recurrence engine. No meter-based/condition-based
triggers (docs/01 already scopes those out of v1). Frontend ships an
agenda-style list (sorted by next due date, pause/resume, create form) —
the month/week calendar grid docs/04 describes is deferred, same
Grid-not-Kanban precedent as M2; not required by this milestone's DoD.

Progress:
- Backend: `PreventiveMaintenance` module (domain: `MaintenancePlan`,
  `MaintenancePlanOccurrence`, `RecurrenceType`; EF Core + migrations,
  site-immutability trigger, cross-schema FK to `identity_access.sites`).
  `MaintenancePlansEndpoints` (create/list/get/pause/resume, `plans.*`
  RBAC). The generation orchestration itself
  (`MaintenancePlanGenerationRunner`/`MaintenancePlanGenerationService`,
  a `BackgroundService` ticking every `PreventiveMaintenance:
  SweepIntervalSeconds` — default 60s, `PreventiveMaintenance:
  SchedulerEnabled` to disable) lives in `Cmms.Api`, not inside the
  `PreventiveMaintenance` module project, to preserve this codebase's
  established "a module never references another module's project"
  boundary — the same reason `MaintenanceRequestsEndpoints.ConvertRequestAsync`
  lives in `Cmms.Api` rather than inside the `MaintenanceRequests` module.
- Verified: `dotnet build`/`dotnet test` on `Cmms.sln` green in Release
  config (17/17 integration tests, real PostgreSQL via Testcontainers);
  frontend `lint`/`build`/`test` (16/16) green; `docker compose up` boots
  API + DB and applies migrations cleanly from a reset volume.
- **Not** verified: same browser-screenshot gap as M1/M2 (no headless
  Chromium available, no user-provisioning endpoint for a non-Admin login);
  see M2's note for the full rationale — unchanged here.

Pending: independent Codex QA re-review (carried over from M2, same
fallback reason); calendar month/week grid.

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
