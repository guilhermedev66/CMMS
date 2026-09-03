# Domain & Workflows

Synthesized from `docs/discovery/antigravity-m0-research.md` (domain research,
Sections 1 & the KPI section) and `docs/discovery/backend-m0-draft.md`
(aggregate/invariant proposal), with scope decisions applied below. Where the
two disagreed, the decision here is authoritative.

## Scope framing

Single company, one deployment, one or more physical **Sites** (plants).
This is *not* a multi-tenant SaaS product — no customer-organization
isolation, no tenant switcher, no per-tenant billing. "Site" exists purely as
a location-scoping concept for RBAC and asset hierarchy (a Planner can be
scoped to specific sites), not a tenant boundary. See
`docs/02-security-and-invariants.md` for why this matters to the threat model.

## Site-boundness (resolves QA finding B-02)

Codex QA's adversarial review flagged that authorization derived from a
*mutable* parent (e.g. an asset's current location) can leak historical
records across sites when that parent moves. Resolution, decided here:

**Decision: no cross-site transfer of Assets, Locations, Requests, Work
Orders, or Maintenance Plans in v1.** Every one of these is permanently
bound to the site it was created in — `site_id` is set once at creation and
never changes. This isn't a limitation forced on us; it's a scope cut, the
same kind as the other lean-scope decisions in this document — cross-site
asset relocation is a real EAM feature, but nothing in this product's brief
asked for it, and cutting it entirely removes B-02's actual attack surface
instead of just mitigating it. (An asset physically moving plants in real
life would be modeled as retiring the record at the old site and creating a
new one at the new site — acceptable for v1.)

What remains, and is still a hard requirement:

- Every table with security relevance (`assets`, `locations`, `requests`,
  `work_orders`, `maintenance_plans`, `maintenance_plan_occurrences`,
  `checklist_responses`, `downtime_intervals`, `part_usages`,
  `attachments`, `audit_events`) carries a `site_id` column, `NOT NULL`,
  copied from its parent aggregate **at creation** and never updated.
- A `Location`'s parent must be the same site (`CHECK`/trigger). An
  `Asset`'s `ParentAssetId` and `CurrentLocationId` must be the same site as
  the asset. A `MaintenancePlan`'s target asset must be the same site as the
  plan. All enforced by composite FK/CHECK constraints referencing
  `site_id`, not just application code.
- Child records (checklist responses, downtime intervals, part usages,
  attachments) are never looked up directly by their own ID for
  authorization purposes — every access goes through their owning Work
  Order (or Asset) root, which is what actually gets the site check.

## Corrective maintenance flow

```
Maintenance Request  --(Planner/Supervisor converts)-->  Work Order
  New                                                       Draft
  Converted / Rejected / Cancelled                          Open
                                                             Scheduled
                                                             InProgress <-> OnHold
                                                             Completed
                                                             Closed
                                                             (Cancelled: Draft|Open|Scheduled|InProgress|OnHold only)
                                                             Reopen: Completed|Closed -> InProgress (new execution cycle)
```

**Maintenance Request** is the intake entity. Anyone with the Requester role
(or higher) can submit one against an asset or a location (if the asset is
unknown). States: `New -> Converted | Rejected | Cancelled`.

Decision: conversion by a Planner/Supervisor **is** the approval step — no
separate `Triaged`/`Approved` intermediate state. This was in the research
draft; simplified here because a second gate adds process weight without a
clear v1 payoff. Converting a Request creates exactly one Work Order
(`converted_work_order_id` on the Request and `source_request_id` on the
Work Order are both unique — enforced by the database, not just application
logic, so a retried conversion can't create a duplicate).

**Resolves QA finding B-04(1) — Convert/Reject/Cancel race on the same
Request.** A Request has its own root-lock protocol, exactly like a Work
Order: `Convert`, `Reject`, and `Cancel` are each a conditional transition —
`UPDATE requests SET status = 'Converted', converted_work_order_id = :wo_id
WHERE id = :req_id AND status = 'New'` (and equivalently for `Reject`/
`Cancel`) — executed inside the *same transaction* that creates the linked
Work Order and writes the audit event. Whichever command's `WHERE` clause
still matches wins; the loser affects zero rows and the application returns
a conflict, not a corrupted linked state.

Decision: no separate `FailureRecord` aggregate. The research draft proposed
splitting "reported symptom" from "confirmed failure" as distinct entities —
valuable in a full EAM product, but for v1 the failure classification (cause
category/mechanism) and downtime interval live directly on the Work Order /
`DowntimeInterval` records. Revisit only if a real requirement for
emergency-failure-without-a-prior-request-tracking-separately appears.

## Work Order lifecycle

Rewritten as a normative transition table per Codex QA finding **B-03**
("the domain doc promises a transition table in ADR-04 but only gives a
diagram"). This table is the actual contract every endpoint, Kanban drag
handler, and job must obey — an operation not listed here is not a legal
transition.

Foundational rules that resolve the ambiguities QA listed:

- **Claiming and starting are always two separate steps.** Assignment
  (self-claim or Planner-assign) always transitions `Open -> Scheduled`.
  "Start Work" always transitions `Scheduled -> InProgress`. There is no
  path that skips `Scheduled`. `Scheduled` requires an assignee; it does
  **not** require a planned date (`planned_date` is an optional field on a
  `Scheduled`/later order, not a state guard) — this resolves the
  self-claim-without-a-date contradiction QA flagged.
- **`Cancelled` is reachable only from `Draft`, `Open`, `Scheduled`,
  `InProgress`, or `OnHold`** — not from `Completed`. To abandon a
  `Completed` order, `Reopen` it first (back to `InProgress`), then
  `Cancel` from there if it's truly being abandoned. This removes the
  "cancelling vs. voiding a completion" ambiguity QA flagged.
- **Reassign / unassign / reschedule** are legal only from `Open`,
  `Scheduled`, or `OnHold` — never directly on an `InProgress` order (put it
  `OnHold` first if the assignment must change mid-execution).
- **`execution_cycle` versioning.** Every Work Order carries an
  `execution_cycle` integer, starting at `1`. `Reopen` increments it. All
  execution-scoped child data (checklist responses, labor/wrench intervals,
  downtime intervals, completion evidence, closure facts) is keyed by
  `(work_order_id, execution_cycle)`. This is what makes `Reopen` safe:
  the prior cycle's completion/closure facts remain readable exactly as
  they were: `Reopen` never overwrites history, it starts a new cycle.
  Floating-plan next-due-date calculation and KPI aggregation use the
  *latest closed cycle's* completion timestamp, not the order's
  first-ever completion.
- **Planner may complete and close the same order they executed.** No
  enforced separation-of-duty for v1 (resolves QA's O-04) — this is a
  documented trade-off, not an oversight: the 4-role model doesn't have a
  distinct "verifier" role, and a small maintenance team often is the same
  person end-to-end. `completed_by` and `closed_by` are always recorded as
  separate audit facts regardless of whether they're the same user, so the
  trade-off is at least visible in the data if it ever needs revisiting.
- **Cancel/Reopen and open intervals.** Any *open* (unclosed) labor or
  downtime interval is force-closed with a system-generated end timestamp
  and an system note when a Work Order is `Cancelled`, or when `Reopen`
  starts a new cycle over a still-open prior interval (shouldn't normally
  happen since `Completed` already requires closed intervals, but the rule
  exists for the cancel-from-`InProgress`/`OnHold` path). Attachments not
  yet linked to a checklist item at cancel/reopen time remain stored and
  linkable to the new cycle by an explicit action — never silently dropped.

| Source state | Command | Target state | Actor / permission | Guard | Side effects |
|---|---|---|---|---|---|
| — | Create (draft) | `Draft` | Planner/Admin | — | audit: created |
| `Draft` | Publish | `Open` | Planner/Admin | scope/asset/site valid | audit |
| `Open` | Self-claim | `Scheduled` | Technician (own site) | conditional `UPDATE ... WHERE assignee IS NULL AND status='Open'` | assignee set, audit: assigned |
| `Open` | Assign | `Scheduled` | Planner/Admin | target technician active at site | assignee set, audit: assigned |
| `Scheduled` | Reschedule / Reassign / Unassign | `Scheduled` / `Open` | Planner/Admin | not `InProgress` | audit |
| `Scheduled` | Start Work | `InProgress` | assignee, or Planner/Admin | assignee active at site | `wrench_start` recorded, audit |
| `InProgress` | Put On Hold | `OnHold` | assignee, or Planner/Admin | reason code required | open wrench interval closed, audit |
| `OnHold` | Resume | `InProgress` | assignee, or Planner/Admin | — | new wrench interval opened, audit |
| `InProgress` | Mark Completed | `Completed` | assignee, or Planner/Admin | all required checklist items resolved; ≥1 labor entry; no open labor/downtime interval; if machine-down, downtime start+end+cause code present | `wrench_end`/completion facts frozen for this cycle, audit, outbox: `WorkOrderCompleted` |
| `Completed` | Close | `Closed` | Planner/Admin | — | closure facts frozen, audit, outbox: `WorkOrderClosed` (KPI recalculation) |
| `Completed`/`Closed` | Reopen | `InProgress` | Planner/Admin | reason required | `execution_cycle += 1`, prior cycle facts retained read-only, audit |
| `Draft`/`Open`/`Scheduled`/`InProgress`/`OnHold` | Cancel | `Cancelled` | Planner/Admin | reason required | open intervals force-closed, audit |

A Work Order has exactly one primary assignee for v1 (no collaborator list —
simplification vs. the research draft's optional multi-assignee model;
revisit only if crew-based work genuinely needs it).

`Completed`, `Closed`, and `Cancelled` orders reject any mutation not listed
in the table above. Corrections after that point are explicit, audited
commands (`Reopen`, or a narrowly-scoped privileged metadata correction —
e.g. fixing a typo in a closed order's description — never a silent edit to
execution facts).

## Preventive maintenance flow

```
MaintenancePlan -> Schedule (Fixed or Floating calendar recurrence)
                -> Background Job generates MaintenancePlanOccurrence + Work Order
                -> Execution
                -> Next occurrence calculated
```

- **Fixed** recurrence: next due date is anchored to the calendar regardless
  of when the prior instance closed (e.g. "1st of every month").
- **Floating** recurrence: next due date is calculated from the actual
  completion date of the prior occurrence (e.g. "30 days after last done").
- `SuppressIfOpen` (default): don't generate a new occurrence while a
  previously generated one for the same plan is still open — flag it as
  overdue instead of stacking duplicates.
- `GenerationLeadTimeDays`: a Work Order due June 15 can be generated June 8
  so parts/scheduling happen ahead of the due date.

Decision: **meter-based** (runtime hours/cycles) and **condition-based**
(telemetry threshold) triggers are explicitly out of scope for v1 — they
require meter-reading ingestion / telemetry infrastructure this product
doesn't otherwise have. Calendar-based (Fixed + Floating) covers the large
majority of real preventive programs and is the whole v1 scheduling model.
The `MaintenancePlan` schema should still carry a `RecurrenceType` field so
meter-based can be added later without a breaking migration.

Duplicate-generation protection (two scheduler ticks, a redeploy restarting
the worker mid-run, a retried job) is a database-level invariant, not a
scheduler-behavior promise — see `docs/02-security-and-invariants.md` §
Concurrency & Invariants.

**Resolves QA finding B-04(2) — `SuppressIfOpen` only protected the single
nominal occurrence date, not "one open generated order per plan" across
different due dates, and wasn't serialized against plan pause/edit or the
floating-date recalculation.** `MaintenancePlan` carries a nullable
`active_occurrence_id` column pointing at its currently-open generated
occurrence/Work Order. The generation job, under a `SELECT ... FOR UPDATE`
lock on the **plan row itself** (distinct from the `FOR UPDATE SKIP LOCKED`
batch-claim used to pick which plans to evaluate this sweep — that part is
about work distribution, not correctness):

1. Re-validates the plan is still `Active` and re-reads its current schedule
   revision under the lock (a pause/edit committed after the batch-claim but
   before this lock is now visible).
2. If `active_occurrence_id IS NOT NULL`, does nothing this due date —
   already covered by `SuppressIfOpen`, regardless of which nominal
   `scheduled_for` it was generated for.
3. Otherwise, in the same transaction: inserts the occurrence (unique
   `(plan_id, scheduled_for)` remains the final safety net even if the lock
   protocol is ever bypassed), creates the Work Order, sets
   `active_occurrence_id`, advances `next_due_at`, writes audit/outbox.

A domain event on the generated Work Order reaching `Completed`, `Closed`,
or `Cancelled` clears `active_occurrence_id` back to `NULL` (itself inside
that Work Order's own root-locked transition, so the plan pointer and the
order's terminal state change together). `Reopen`-ing that Work Order after
the plan already generated its next occurrence is accepted as a known edge
case for v1: the reopened order and the newly generated one can briefly
coexist — flagged to the Planner via the "overdue"/duplicate-alert UI rather
than prevented outright, since fully preventing it requires locking the plan
during an unrelated Work Order's reopen. Documented, not silently ignored.

## Asset & location hierarchy

Two entities, not the full 7-level ISA-95 tree from the research draft
(descoped — that level of hierarchy is real for a multi-plant industrial
conglomerate, not a portfolio-scoped v1):

- **Location**: recursive tree (`ParentLocationId`), e.g. `Site > Area >
  Line/Cell`. Represents plant topology, not physical hardware.
- **Asset**: physical equipment record (tag, name, category, manufacturer,
  model, serial number, criticality, operational status, `CurrentLocationId`,
  optional `ParentAssetId` for sub-components, e.g. a motor that belongs to a
  pump skid).

Decision: no separate temporal `AssetInstallationHistory` ledger for v1.
Reassigning an asset's location is an audited mutation (via the standard
audit log) rather than a bespoke history table — simpler, and the audit log
already gives a "what changed and when" trail. Revisit if rotating-spare
tracking (pull a motor for overhaul, install a spare, return the original)
becomes a real requirement.

**Criticality** (ABC classification): `A` (critical — production stopper /
safety / single point of failure, strict PM compliance required), `B`
(essential — redundant or buffered, degrades production in 24–48h), `C`
(non-critical — run-to-failure acceptable).

## Checklist item types

`Boolean` (pass/fail, optional `safety_critical` flag — covers LOTO-style
sign-off without a separate item type), `Numeric` (value + min/max
tolerance + unit, flagged amber/red outside bounds), `SingleSelect`
(predefined options), `PhotoRequired` (must attach an image before the item
can be marked done), `Note` (free text). The checklist *definition* is a
versioned template; a Work Order snapshots the template version and items at
creation time so a later template edit never rewrites history.

## Downtime tracking

Not a single mutable total — a set of intervals per Work Order/Asset.
`started_at` / `ended_at` (`timestamptz`), classification `FullStop` vs.
`PartialDerating`, and a two-level cause code (`Category`:
Mechanical/Electrical/Hydraulic/Pneumatic/Instrumentation/Operational;
`Mechanism`: Wear/Contamination/ThermalOverload/Misalignment/LooseFastener/
SoftwareFault, etc.). At most one open interval per asset at a time
(partial unique index). A corrective Work Order that represents a
machine-down event cannot close without `started_at`/`ended_at` and a cause
code recorded — this is what makes MTTR/MTBF numbers below defensible
instead of fabricated.

**Resolves QA finding O-02.** A partial unique index only stops two
*currently-open* intervals for the same asset; it doesn't stop two already-
closed intervals from overlapping in time, which would silently double-count
downtime in the MTTR/availability formulas below — and because different
Work Orders can each open a downtime interval against the same asset, the
per-Work-Order root lock alone doesn't serialize this. Decision:
`FullStop` intervals get a PostgreSQL exclusion constraint (`EXCLUDE USING
gist (asset_id WITH =, tstzrange(started_at, ended_at) WITH &&)`, via the
`btree_gist` extension) so two `FullStop` intervals for the same asset can
never overlap, full stop (pun acknowledged). `PartialDerating` intervals are
allowed to overlap by design (two lines can be derated in parallel) and are
documented as summed, not deduplicated, in downtime rollups.

## Parts & costs (lean scope)

Decision: **record-only**, not a stock/warehouse/reservation system. A
`PartUsage` entry (part name/code, quantity, unit cost snapshot, currency,
Work Order link, actor, timestamp) is an immutable ledger row. No stock
level tracking, no bin/warehouse location, no reservation workflow, no
costing-method choice (standard/moving-average) — those are real ERP/MRO
inventory features explicitly out of scope per the project brief ("peças/
custos em escopo enxuto", no "advanced inventory"). This still supports
real cost-per-work-order and cost-by-asset reporting; it just doesn't try to
be a warehouse management system.

## KPI formulas (must be mathematically defensible)

Sourced from SMRP Best Practice Guide / ISO 14224 / EN 13306 definitions,
not invented. Store raw operational timestamps on transactional rows
(`downtime_start`, `downtime_end`, `wrench_start`, `wrench_end`,
`planned_hours`, `actual_hours`, `part_unit_cost`); never persist
pre-computed averages — compute on demand or via a rebuildable read
projection.

**MTBF** (Mean Time Between Failures) — operating time between unscheduled
failures, *not* calendar time divided by failure count:
```
MTBF = (Total Available Time − Total Downtime) / Count of Failure Work Orders
```
Undefined (or reported as "≥ operating hours, zero-failure period") when the
failure count is zero — never silently rendered as `0` or `∞`.

**MTTR** (Mean Time To Repair) — active repair time only, not total downtime:
```
MTTR = Σ(repair_complete − repair_start) / k
MDT (Mean Downtime) = Σ(production_handover − breakdown_stop) / k   -- includes logistics/parts wait
```

**Availability**:
```
Operational Availability  Ao = Actual Operating Time / Planned Production Time
Inherent Availability     Ai = MTBF / (MTBF + MTTR)
```

**Backlog** (in crew-weeks, not a raw open-ticket count):
```
Backlog(weeks) = Σ(Estimated Labor Hours, open WOs) / (Available Weekly Craft Hours × Productivity Factor)
Available Weekly Craft Hours = Technicians × Shift Hours/Week − (Vacation + Training + PTO)
Productivity Factor ≈ 0.65–0.75 (accounts for travel/prep/meetings)
```
Healthy range: 2–4 weeks; `<2` suggests overstaffing/uncaptured work,
`>6` suggests dangerous deferral.

**Planned Maintenance Percentage**:
```
PMP = Labor Hours on Planned PM / Total Labor Hours (PM + Reactive)  × 100%
```
World-class target: ≥80% planned, ≤20% reactive.

**Cost**:
```
Total Cost of Maintenance = Σ(Labor Hours × Loaded Rate) + Σ(Parts × Unit Cost) + Contractor Invoices
% RAV = Annual Maintenance Cost / Estimated Asset Replacement Value × 100%
```
World-class target: 2.0–3.5% of RAV. `% RAV` is reportable only once asset
replacement-value fields exist — mark as a stretch metric if that field
isn't populated in early milestones.

## Open items carried to M1

- Confirm asset-code uniqueness scope: site-local vs. company-wide (default
  assumption: company-wide, simpler; revisit if two sites plausibly reuse
  tag numbering).
- Confirm whether a technician may self-claim unassigned Work Orders within
  their site scope, or assignment is always Planner-driven (default
  assumption for M2: self-claim allowed for unassigned Open/Scheduled
  orders in the technician's site — this is also the natural scenario for
  the concurrent-claim invariant test).
