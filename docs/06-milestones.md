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

Additional gaps found on a post-hoc self-audit of this milestone (fixed one
stale doc-comment cross-reference in `MaintenancePlansEndpoints.cs` left
over from moving the generation runner into `Cmms.Api`; these three are
scope notes, not fixed, since none are in the DoD above): no plan-edit
endpoint (create/pause/resume only — changing a plan's interval/asset/title
after creation isn't supported yet); due dates are plain UTC instants with
no site-local calendar semantics (a `Site` has a `TimeZone` field, but
`MaintenancePlan.NextDueAtUtc` doesn't use it — consistent with the
already-documented day-interval-only recurrence simplification, just not
previously spelled out for timezones specifically); the Reopen/
already-regenerated coexistence edge case docs/01 explicitly accepts
("flagged to the Planner via the overdue/duplicate-alert UI rather than
prevented outright") has no such alert UI built yet — the underlying
behavior (both orders can coexist, neither corrupts plan state) is correct
and covered by `ClearActiveOccurrence`'s idempotent guard, just not
surfaced visually.

## M4 — Maintenance Execution — **PASS**

Technician workflow, checklist execution, downtime capture, parts/costs
(lean scope), attachments, QR-driven navigation (Asset → Work Order →
Checklist/Notes/Evidence → Complete), mobile UX, attachment/QR security
enforcement.

DoD check: QR scan never grants authorization beyond what the technician's
role already allows — PASS, proven by two negative tests
(`AttachmentsAndQrSecurityTests.Scanning_an_asset_qr_locator_while_unauthenticated_returns_401_not_asset_data`
and `..._grants_exactly_the_same_visibility_ordinary_asset_read_would`, the
latter asserting a same-site scan returns the asset and a *different*-site
scan of the identical tag returns 404, never asset data). Attachment upload
rejects path traversal / disallowed types / oversized files — PASS: a
storage-layer unit test (`LocalDiskAttachmentStorage_rejects_any_key_that_is_not_its_own_generated_format`,
5 malicious-key cases) proves path traversal is refused before any I/O, one
integration test proves a declared type outside `image/jpeg|png|webp`
(including `image/svg+xml`) is rejected at intent-creation before any bytes
are even accepted, one proves >15MB is rejected as `413` before decode, and
one proves bytes that aren't actually a decodable image are rejected `422`
at finalize regardless of their declared type (ImageSharp's own decoder
failure *is* the magic-byte check — ADR/QA framing in
`src/Cmms.Api/AttachmentsEndpoints.cs`). Full mobile flow — **not**
independently verified live (same headless-Chromium gap as M1–M3: no
interactive `sudo`/GUI session available to this agent non-interactively);
verified instead by code review (every execution-panel control uses this
codebase's established responsive Tailwind patterns, `capture="environment"`
on file inputs for camera capture, no fixed-width layout) — tracked to
revisit with a real device/browser pass at M6, same as the carried-over M1–M3
item. Independent QA pass — unavailable this session (fallback protocol,
same reason as M1–M3): the orchestrator ran an adversarial self-review pass
instead (see "Backend" below for what it caught) — tracked as pending
independent re-review, same as M2/M3's still-open item.

Design:
- **Checklist** (`src/Modules/WorkManagement/Domain/ChecklistItem.cs`):
  five item types (Boolean w/ `safety_critical`, Numeric w/ tolerance band,
  SingleSelect, PhotoRequired, Note), keyed by `(work_order_id,
  execution_cycle)` per docs/01. No separate template entity — items are
  defined directly on the Work Order (Planner/Admin only,
  `workorders.plan`), the assignee only resolves them (`workorders.execute`).
- **Downtime** (`DowntimeInterval.cs`): open/close intervals, `FullStop` vs
  `PartialDerating`. A PostgreSQL exclusion constraint
  (`ex_downtime_intervals_fullstop_no_overlap`, `btree_gist`) makes two
  overlapping FullStop intervals on the same asset impossible regardless of
  open/closed state — not just "no two open at once" — proven by
  `Two_overlapping_fullstop_downtime_intervals_on_the_same_asset_are_rejected_by_the_database`
  (second `POST` returns `409`, not `500`).
- **Parts** (`PartUsage.cs`): immutable ledger row, no stock levels
  (lean scope per docs/01). Client-supplied idempotency key deduplicates a
  retried posting — proven by
  `Part_usage_idempotency_key_deduplicates_a_retried_posting` (same id
  returned on replay, exactly one row persisted). Costs (`unitCost`/
  `currency`) are masked to `null` in the API response for a caller without
  `costs.view` (Technician isn't seeded that permission) — proven by
  `Costs_are_masked_for_a_caller_without_costs_view_permission`.
- **Mark Completed guard** (`WorkOrder.MarkCompleted`, docs/01's transition
  table): now enforces "all required checklist items resolved for this
  execution cycle" and "no open downtime interval for this execution cycle"
  — both computed by the endpoint under the same root lock that the
  transition itself takes (docs/02: "Child edit races with completion ...
  both commands lock the Work Order root"), proven by
  `Mark_completed_is_rejected_while_a_required_checklist_item_is_unresolved`
  and `..._while_a_downtime_interval_is_still_open` (409 before, 200 after
  resolving/closing). **Scope cut, documented inline on the method**:
  docs/01 also lists "≥1 labor entry" in the same guard; this slice has no
  per-entry labor ledger, only the single `wrench_start_at_utc` timestamp
  already set by Start Work — "labor recorded" degrades to "work was
  started" for v1, not "an itemized labor entry exists."
- **Attachments** (`src/Modules/Attachments/`, new module + schema):
  the full 5-step pipeline from docs/02 § "Attachment strategy" —
  server-generated quarantine key → client uploads bytes → finalize
  re-authorizes against the Work Order's *current* state, verifies actual
  size/format against the intent (not the client's claim), decodes with
  ImageSharp, strips EXIF/ICC/XMP, re-encodes to a separate server-generated
  "clean" key the client never had write access to; the quarantine key is
  deleted only after that commits. **Documented substitution** (see
  `AttachmentUploadIntent`'s doc comment): bytes flow through this API
  server rather than a presigned direct-to-R2 PUT, since this dev/CI
  environment has no object-storage credentials an agent can provision
  without the account owner — every security property docs/02 specifies is
  still upheld (server-generated keys, re-authorize-at-finalize, mandatory
  re-encode, no client write access to the clean key); swapping
  `IAttachmentStorage` for a real S3/R2-backed implementation at deploy time
  is a drop-in change behind that interface. `PhotoRequired` checklist
  resolution re-verifies the referenced attachment is still `Active`/linked
  to *this* Work Order at resolve time (docs/02's async-validation race
  guard), proven end-to-end by
  `Full_upload_finalize_flow_produces_an_active_attachment_that_satisfies_a_photorequired_item`.
  Downloads set `Content-Disposition: attachment` and
  `X-Content-Type-Options: nosniff`; a cross-site download attempt returns
  `404` (`A_technician_from_a_different_site_cannot_download_another_sites_attachment`).
  Malware/AV scanning remains an explicit, already-documented optional M6
  hardening item (docs/02), not required for this DoD.
- **QR** (`Asset.QrLocator`, scaffolded in M1, wired end-to-end now):
  `GET /assets/by-qr/{qrLocator}` is byte-for-byte the same RBAC as
  `GET /assets/{id}` — there is no separate "QR capability," the frontend's
  `/scan/:qrLocator` route (outside `<ProtectedRoute>`, since it must do its
  own auth-check-then-redirect) is a deep link into that same authorized
  query keyed by one more field. No public/anonymous "report an issue"
  intake was built — it was explicitly scoped as an *optional* M4 stretch in
  docs/02, not required for this DoD.

Progress:
- Backend: `Attachments` module (domain, EF Core + migrations, site-
  immutability trigger, `LocalDiskAttachmentStorage` with regex + resolve-
  and-recheck path-containment defense), wired into `Cmms.Api`/`Cmms.sln`/
  Docker build/`docker-compose.yml` (a dedicated named volume, owned by the
  non-root `app` user at image-build time). `WorkOrderExecutionEndpoints.cs`
  (checklist/downtime/parts) and `AttachmentsEndpoints.cs`
  (upload-intent/bytes/finalize/download/unlink) in `Cmms.Api`, following
  this codebase's established root-lock-then-authorize-then-mutate-then-
  audit protocol. `GetAssetByQrLocatorAsync` added to `AssetsEndpoints.cs`;
  `ListWorkOrdersAsync` gained an `assetId` narrowing filter (RBAC-scoped
  query, not a new visibility path) for the QR → asset → related-orders
  flow. No new `PermissionCatalog` entries needed for checklist/downtime/
  parts — they reuse `workorders.execute`/`workorders.plan`/
  `workorders.complete`, matching docs/02's framing of child data as
  Work-Order-root-scoped, not independently permissioned; `attachments.*`
  permissions already existed in the catalog (seeded M1, unused until now).
- Self-review caught and fixed before commit (documented here per this
  project's "disclose gaps found on review" convention, same as M3's
  post-hoc audit): (1) two attachment endpoints (`bytes`, `finalize`) were
  initially missing the anti-forgery check every other mutating endpoint in
  this codebase has; (2) `AttachmentsEndpoints`' image-rejection paths
  called `SaveChangesAsync` inside a transaction the method then returned
  from *without* committing — `SharedTransactionScope`'s dispose-time
  rollback would have silently undone the `Rejected` status write; (3) the
  downtime/parts exclusion-constraint violation only actually throws at
  `SaveChangesAsync`, which was outside the `try/catch` meant to catch it
  (the mutate delegate itself only stages an `Add`); (4) the download
  endpoint's doc comment claimed `X-Content-Type-Options: nosniff` was set
  when it wasn't — the header is now actually appended. All four fixed and
  covered by the tests listed above before this commit, not after.
- Frontend: `WorkOrderExecutionPanel.tsx` (Overview/Execution tabs on the
  Work Order detail page — checklist per-item-type controls incl. camera
  capture for `PhotoRequired`, downtime open/close, parts entry with
  cost-masking-aware rendering, an evidence-photo gallery with unlink), and
  `ScanPage.tsx` (`/scan/:qrLocator`, reusing `ProtectedRoute`/`LoginPage`'s
  existing return-URL shape rather than inventing a second one). No new UI
  library, no optimistic UI on any security/state-relevant action —
  every mutation re-fetches server-confirmed state, matching this
  codebase's established M2/M3 convention.
- Verified: `dotnet build` (Debug and Release) on `Cmms.sln` green, 0
  warnings; both new EF Core models confirmed to have zero pending model
  changes against their migrations (`dotnet ef migrations
  has-pending-model-changes`, run independently for `WorkManagement` and
  `Attachments`) — a real check, not an assumption. Frontend
  `lint`/`build`(`tsc -b && vite build`)/`test` (16/16) green, run and
  independently re-verified by the orchestrator, not only self-reported by
  the agent that wrote the frontend.
- **Not verified locally**: the new PostgreSQL/Testcontainers integration
  tests themselves (11 new tests across `WorkOrderExecutionTests.cs` and
  `AttachmentsAndQrSecurityTests.cs`) — Docker Desktop's daemon is not
  running in this environment and starting it needs an interactive Windows
  GUI session this agent cannot supply non-interactively (the same class of
  environment gap as M1–M3's headless-Chromium blocker, not a new kind of
  issue). They **did** run successfully in CI (GitHub Actions' `ubuntu-
  latest` runners ship Docker) — see the commit's CI run linked from this
  repo's Actions tab; treat that as the real verification of this batch of
  tests, not local execution. `docker build`/`docker compose up` were also
  not exercised locally for the same reason — CI does not currently build
  the Docker image at all (pre-existing gap since M1, not introduced here);
  tracked to close at M6 alongside the deploy step, which will need a real
  image build regardless.

Pending: independent Codex QA re-review (carried over, same as M2/M3);
real-device/browser mobile visual pass (carried over, same as M1–M3); a CI
step that actually builds the Docker image (new item, tracked for M6);
public "report an issue" QR stretch feature (optional, out of scope).

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
