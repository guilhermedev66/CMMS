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

## Corrective maintenance flow

```
Maintenance Request  --(Planner/Supervisor converts)-->  Work Order
  New                                                       Draft
  Converted / Rejected / Cancelled                          Open
                                                             Scheduled
                                                             InProgress <-> OnHold
                                                             Completed
                                                             Closed
                                                             (Cancelled from any non-closed state)
                                                             Reopen: Completed|Closed -> InProgress|Open
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

Decision: no separate `FailureRecord` aggregate. The research draft proposed
splitting "reported symptom" from "confirmed failure" as distinct entities —
valuable in a full EAM product, but for v1 the failure classification (cause
category/mechanism) and downtime interval live directly on the Work Order /
`DowntimeInterval` records. Revisit only if a real requirement for
emergency-failure-without-a-prior-request-tracking-separately appears.

## Work Order lifecycle

States: `Draft` (planner authoring, not on the dispatch board) `-> Open`
(backlog, ready to schedule) `-> Scheduled` (assignee + planned date set)
`-> InProgress` (technician started) `<-> OnHold` (reason required:
`AwaitingParts | AwaitingProduction | AwaitingContractor | Safety | Other`)
`-> Completed` (technician done, checklist satisfied, labor logged — pending
verification) `-> Closed` (supervisor verified; immutable ledger record).
`Cancelled` is reachable from any non-closed state and requires a reason.
`Reopen` is not a resting state — it's an audited command from `Completed`
or `Closed` back to `InProgress` (or `Open` if replanning is needed),
recording who/when/why and the prior completion/closure facts.

A Work Order has exactly one primary assignee for v1 (no collaborator list —
simplification vs. the research draft's optional multi-assignee model;
revisit only if crew-based work genuinely needs it).

`Completed`, `Closed`, and `Cancelled` orders reject ordinary mutation.
Corrections after that point are explicit, audited commands (reopen, or a
privileged metadata correction), never silent edits.

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
