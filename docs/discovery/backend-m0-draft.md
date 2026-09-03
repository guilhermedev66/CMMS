# M0 Backend Architecture Proposal — CMMS

**Status:** proposed for orchestration review  
**Scope:** bounded backend discovery; no implementation or scaffolding  
**Target stack:** C#, .NET 10, ASP.NET Core, EF Core, PostgreSQL  
**Assumption:** B2B multi-tenant product; an `Organization` is the tenant and contains one or more `Site`s.

## 1. Executive decisions

1. Start as a **modular monolith** with one API host, one worker host, one PostgreSQL database, and schema-owned modules. Do not start with microservices.
2. Make **Work Order** the lifecycle authority, but do not put every concern inside one giant aggregate. Checklist execution, inventory postings, downtime, attachments, and audit records have independent write patterns and are coordinated through the Work Order root.
3. Keep **lifecycle state, assignment, and schedule separate**. “Assigned” and “Reopened” are not durable lifecycle states: assignment is a relationship; reopen is an audited transition back to an actionable state.
4. Use **explicit PostgreSQL constraints plus atomic conditional writes** for uniqueness and races. Use C# for state-machine and cross-record business rules, always under a consistent Work Order row-lock protocol where child edits can race with completion/closure.
5. Assume at-least-once delivery for HTTP retries and background jobs. Prevent duplicates with a **business uniqueness constraint** for preventive occurrences and narrowly scoped idempotency keys at true retry boundaries.
6. Treat authorization as `permission + tenant + resource scope`. A valid identifier, asset tag, barcode, or QR code is never proof of authorization.
7. Record high-value audit events explicitly and transactionally with the business change. Build Asset History as a projection of authoritative Work Order and audit/domain events, not as a second mutable ledger.

## 2. Solution and module boundaries

Suggested logical layout; the names are guidance, not a request to scaffold now:

```text
Cmms.sln
src/
  Cmms.Api/                     HTTP endpoints, authn, policy wiring, composition
  Cmms.Worker/                  schedules, outbox dispatch, projections, notifications
  Cmms.BuildingBlocks/          IDs, clock, result/errors, transactions, outbox primitives
  Modules/
    IdentityAccess/             organizations, sites, users, memberships, roles/scopes
    Assets/                     asset registry, hierarchy, tags/QR locators, criticality
    MaintenanceRequests/        intake, triage, approval/rejection, conversion
    WorkManagement/             work orders, assignments, labor, checklists, downtime
    PreventiveMaintenance/      plans, schedule calculation, occurrence generation
    InventoryCosting/           stock, part issue/return, cost snapshots, adjustments
    Files/                      attachment metadata, scan state, secure object-store access
    Audit/                      immutable audit event store and history queries
tests/
  Unit/
  Integration/                  real PostgreSQL; constraints and transaction races
  Architecture/                 dependency and module ownership tests
docs/discovery/
```

Initially use one assembly per module with `Domain`, `Application`, and `Infrastructure` folders; split assemblies only when build/dependency pressure justifies it. Give each module its own PostgreSQL schema and EF Core `DbContext`/migrations history. A module writes only its own tables. Cross-module access uses small application contracts; projections and notifications use transactional outbox events.

Cross-schema foreign keys are acceptable for foundational integrity (`organization`, `site`, `asset`, `work_order`), despite some coupling. Where one command must atomically touch two owners—preventive occurrence plus generated Work Order, or stock issue plus Part Usage—share the same PostgreSQL connection/transaction through an explicit orchestrator. Do not introduce a message broker merely to avoid a local transaction.

Every business row carries `organization_id`; site-bound rows also carry `site_id`. Use composite foreign keys or referenced composite unique keys such as `(organization_id, asset_id)` so a programming error cannot create a cross-tenant relationship. EF query filters are convenience, not the security boundary. PostgreSQL row-level security can be added as defense in depth if direct database access or the SaaS risk profile warrants it; if used, tenant context must be set with `SET LOCAL` inside every transaction so pooled connections cannot leak context.

## 3. Domain model and invariants

### 3.1 Asset

The Asset aggregate owns identity, organization/site, asset code, type/category, location, parent, operational status, criticality, commissioning/retirement facts, and tag/QR locator references.

**Database-protected**

- `organization_id`, `site_id`, asset code, status, and criticality are non-null; codes are unique in the agreed scope (proposed: `(organization_id, asset_code)`).
- Tenant-safe FKs for site, parent, category, and location; an asset cannot parent itself.
- QR/tag locator is a high-entropy opaque value with a unique index. Do not encode a sequential ID or permission-bearing claim.
- Enumerated values have migration-owned `CHECK` constraints; timestamps use `timestamptz`; retirement cannot precede commissioning.

**C# under transaction**

- Parent hierarchy must be acyclic and remain within the same organization/site policy.
- Retired assets cannot receive new normal work, while historical records remain readable; exceptional reactivation is a privileged audited command.
- Criticality and operational-status changes require an explicit reason and audit event.

### 3.2 Maintenance Request

States: `New -> Triaged -> Approved | Rejected | Cancelled`; `Approved -> Converted`. Conversion links to exactly one Work Order. If the business later permits one request to spawn multiple Work Orders, introduce an explicit split model rather than weakening this invariant silently.

**Database-protected**

- Valid state values, tenant-safe asset/site references, required description/reporter facts.
- A nullable `converted_work_order_id` is unique, and a Work Order's `source_request_id` is unique. This makes conversion one-to-one and prevents retry duplicates.
- Optional external/request-submission idempotency is unique by `(organization_id, operation, idempotency_key)`.

**C# under a locked request row**

- Only allowed transitions occur; rejection/cancellation requires a reason.
- Conversion validates approval and asset eligibility, creates the Work Order, links both sides, and appends audit/outbox records in one transaction.

#### Failure record

A reported symptom is not automatically a confirmed asset failure. Keep request intake separate from a `FailureRecord`, created when loss/degradation of function is confirmed. It records asset, observed start/restoration times, failure mode/cause codes, effect, detection method, downtime link, and the resolving Work Order. This allows emergency failures without a prior request and requests that turn out not to be failures.

The database enforces tenant-safe links, sensible time ordering, and at most one Failure Record for a source request unless the product explicitly adopts multi-failure intake. C# controls `Reported -> Confirmed -> Resolved | Invalidated`, requires a reason/cause at the configured stage, and resolves it in the same locked Work Order transaction as completion/closure when applicable. Correcting failure classification is an audited revision, not history replacement.

### 3.3 Work Order

Proposed lifecycle:

```text
Draft -> Open -> Planned -> Scheduled -> InProgress <-> OnHold -> Completed -> Closed
           \          \          \             \          \-> Cancelled
            \---------- optional planning/scheduling steps ----------------/

Completed or Closed --reopen(reason)--> InProgress (or Open if replanning is required)
```

- `Draft`: incomplete authoring; not dispatchable.
- `Open`: approved backlog, ready for planning or direct execution.
- `Planned`: scope/checklist/resource estimates prepared.
- `Scheduled`: committed execution window.
- `InProgress`: execution is active.
- `OnHold`: execution paused with reason.
- `Completed`: technician has declared execution complete; execution details are frozen pending verification.
- `Closed`: supervisor/manager verified; operational and costing inputs are final.
- `Cancelled`: terminal unless a privileged replacement/new Work Order is created.
- **Assigned is not a status**; a Work Order may have a primary assignee and collaborators in several actionable states.
- **Reopened is not a resting status**; it is an audited command/event that moves the order to `InProgress` or `Open` and records who, when, why, and the prior completion/closure.

The Work Order root owns status, type (`Corrective`, `Preventive`, initially), priority, asset, summary/scope, dates, primary assignee, schedule, completion/closure facts, and an explicit monotonically increasing `row_version`.

**Database-protected**

- Tenant-safe asset, site, request, plan-occurrence, and user references; valid status/type/priority values.
- One Work Order per source request and one per preventive occurrence.
- Required dates and simple ordering checks; non-negative numeric amounts; three-letter currency when cost is present.
- `row_version` participates in conditional updates. Return `409 Conflict` or `412 Precondition Failed` for stale client edits.
- Primary assignment is held on the root for an atomic claim. Collaborator assignments have a partial unique index such as `(work_order_id, user_id) WHERE released_at IS NULL`.

**C# under a locked Work Order row**

- The state machine; mandatory transition reasons; role/scope checks; readiness rules.
- Completion requires required checklist items resolved, required readings/signatures present, downtime intervals valid/closed, and labor/part entries internally consistent.
- Closure requires `Completed`, supervisor permission, and any required cost/verification review.
- `Completed`, `Closed`, and `Cancelled` orders reject ordinary scope, checklist, labor, downtime, part, and attachment-link mutations. Corrections are explicit commands (reopen, reversal, or privileged metadata correction), never silent edits.
- A preventive Work Order stores the plan/template version and a snapshot of execution instructions. Later plan edits never rewrite an existing Work Order.

### 3.4 Assignment and claim

Use an atomic conditional update, not a read-then-write:

```sql
UPDATE work.work_orders
SET primary_assignee_id = :user_id,
    assigned_at = now(),
    row_version = row_version + 1
WHERE organization_id = :org_id
  AND id = :work_order_id
  AND primary_assignee_id IS NULL
  AND status IN ('Open', 'Planned', 'Scheduled')
RETURNING id, row_version;
```

Exactly one of two claimers gets a row. Zero rows means “already claimed or no longer claimable,” not a server error. Reassignment/unassignment is a separate privileged command with history. Assignment does not automatically imply permission to edit every field.

### 3.5 Checklist execution

The plan or Work Order stores an immutable/versioned checklist definition snapshot; the execution stores responses keyed by stable item IDs. Define response columns/types explicitly where constraints matter rather than accepting unrestricted JSON.

**Database-protected:** one response per `(work_order_id, checklist_item_id)`, tenant-safe references, allowed response kind, required basic shape/ranges where expressible, and optimistic version on a response.

**C#:** conditional items, evidence requirements, reading tolerances, and “all required items satisfied” rules. Every checklist mutation first locks the Work Order root and confirms it is editable; this serializes it against completion/closure.

### 3.6 Part Usage and inventory

Treat stock movement as an immutable ledger. A Part Usage is an issue/return/reversal entry linked to a Work Order and carries quantity, unit, warehouse/bin, unit-cost snapshot, currency, actor, and timestamp. Never update an accumulated Work Order part-total with read/modify/write; calculate it from postings or maintain a rebuildable projection.

**Database-protected**

- Quantity is positive; movement direction/type is constrained; references are tenant-safe.
- Client operation ID/idempotency key is unique in its organization/operation scope when a posting may be retried.
- Stock decrement is an atomic conditional update (`quantity_on_hand >= quantity`) or occurs after locking the stock row. Ledger entry, stock balance, Work Order link, audit, and outbox commit together.
- Corrections use compensating return/reversal entries tied uniquely to the original; posted rows are not edited or deleted.

**C# under transaction:** unit conversion, costing policy, reservation policy, Work Order editability, and authorization. Always lock Work Order first, then stock rows in stable ID order to limit deadlocks. Concurrent postings either both fit available stock and commit, or one receives a domain conflict; no lost update or negative stock.

### 3.7 Downtime, labor, attachments, and costs

- Downtime is a set of intervals, not one mutable total. Enforce `ended_at > started_at`; optionally use a PostgreSQL exclusion constraint on time ranges to prevent overlapping intervals for the same asset. Permit at most one open interval per relevant scope with a partial unique index.
- Labor entries have positive durations and tenant-safe worker references. Decide before M1 whether time overlap across different Work Orders is prohibited or merely warned.
- Cost inputs are immutable postings or versioned estimates; use `numeric`, never floating point, and store currency. Derived totals are projections.
- Attachment rows contain metadata and object-store keys only. Object keys are tenant-prefixed and server-generated. Upload completion is accepted only after size/type/hash validation and malware-scan state; access uses short-lived server-authorized URLs.
- Mutations in these child records follow the same “lock Work Order root, verify editable, then write” protocol.

### 3.8 Maintenance Plan and schedule occurrence

The Maintenance Plan aggregate owns target asset(s), plan state (`Draft`, `Active`, `Paused`, `Retired`), scheduling rule/timezone, generation lead time, default priority/trade, and versioned instruction/checklist template. A generated `MaintenancePlanOccurrence` is the durable identity of one expected execution and links to exactly one preventive Work Order.

**Database-protected:** tenant-safe asset/site links, valid plan state and schedule kind, non-negative lead time, optimistic `row_version`, unique plan-version numbers, unique `(organization_id, plan_id, scheduled_for)`, and unique Work Order `source_occurrence_id`.

**C# under a locked plan row:** schedule expressions and timezone must be valid; activation requires a complete target/template; pause/retire stops future generation without deleting history; edits that affect execution create a new template/rule version; next occurrence is calculated deterministically. Occurrence insertion, Work Order creation, `next_due_at` advancement, audit, and outbox records commit together.

## 4. Concurrency and transaction decisions

PostgreSQL constraints are the final authority for uniqueness, referential integrity, non-negative/simple range checks, and business identities. C# owns semantic transitions and policy. Rules involving the Work Order and its children use this transaction protocol:

1. Begin transaction.
2. Load the Work Order root with `SELECT ... FOR UPDATE`.
3. Authorize against tenant/resource scope and validate current status.
4. Lock any additional shared rows in deterministic order, then validate and mutate.
5. Insert the semantic audit event and outbox message in the same transaction.
6. Increment `row_version` and commit.

This produces the following race outcomes:

| Race | Correctness mechanism | Outcome |
|---|---|---|
| Two technicians claim one Work Order | Atomic predicate update on unassigned root | One succeeds; one gets conflict |
| Two clients complete concurrently | Root row lock plus expected status/version | One transition and one completion event; loser sees already completed/stale |
| Child edit races with completion/closure | Both commands lock root before inspecting/editing child data | Edit is entirely before completion or rejected after it; no partial closure |
| Preventive job fires twice | Unique `(plan_id, scheduled_for)` occurrence plus atomic generation transaction | One occurrence and one Work Order |
| Two scheduler instances race | Claim due rows using short transaction and `FOR UPDATE SKIP LOCKED`; business unique constraint remains the safety net | Work is shared; crash/retry cannot duplicate output |
| Retry after ambiguous Work Order creation | Source uniqueness or scoped idempotency record stores request hash and result | Same request returns prior result; changed payload with same key is conflict |
| Concurrent part usage | Work Order/root check plus conditional stock update/row lock and immutable ledger | No lost decrement, over-issue, or duplicate retry |

Do not rely on in-memory locks, a singleton worker, scheduler leases, or “exactly once” messaging for correctness. Keep database transactions short; do not call object storage, email, or other network services while holding locks. Commit an outbox message and perform those effects afterward.

### Preventive scheduling algorithm

Represent each due instance as a durable `maintenance_plan_occurrence` with `(organization_id, plan_id, scheduled_for)` unique. A worker:

1. Claims a bounded batch of due plans/occurrences with `FOR UPDATE SKIP LOCKED` and a short lease for operational visibility.
2. In one transaction, inserts the occurrence (or observes the unique conflict), creates the Work Order with `source_occurrence_id` unique, advances `next_due_at`, and writes audit/outbox records.
3. On retry, loads and returns the already-created Work Order.

The lease improves throughput and recovery; uniqueness provides correctness. Store schedule timezone and local rule, but persist instants as UTC. Define DST behavior explicitly. Advance the next due date from the intended scheduled occurrence, not from worker run time, unless the plan policy explicitly says “after completion.” Meter-based plans should use the same occurrence identity idea but can follow after the initial time-based implementation.

## 5. Idempotency: where it is and is not justified

Use idempotency only where callers or workers can legitimately retry a **non-idempotent creation/posting** after an ambiguous response:

- Public/mobile creation of Maintenance Requests and direct Work Orders.
- Converting a request when the caller can retry (also protected by source-request uniqueness).
- Preventive generation, using the natural occurrence key—not a generic random key.
- Inventory issue/return/reversal postings.
- External webhook/event ingestion and attachment upload finalization if those integrations retry.

Generic HTTP idempotency records should be unique by `(organization_id, operation_name, key)`, store a canonical request hash and the stable result ID/status, and reject reuse with a different payload. Keys must not cross users/tenants or authorize the replayed result.

Do **not** require keys for reads, deletes that target a known resource, ordinary versioned edits, atomic claim, or lifecycle transitions guarded by expected state/version. Those operations already have stable resource identity and conditional semantics. A command ID may still be useful for end-to-end tracing, but that is not a reason to build a deduplication table for every command.

## 6. RBAC and resource scopes

Recommended built-in roles are permission bundles, not hard-coded branches. Allow custom roles later. Scope every grant to organization and, where relevant, sites/teams/self/assigned Work Orders.

| Capability | Org Admin | Maintenance Manager | Planner / Supervisor | Technician | Requester | Inventory Clerk | Auditor |
|---|---:|---:|---:|---:|---:|---:|---:|
| Manage users, roles, sites, settings | Yes | No | No | No | No | No | Read |
| Create/edit assets and criticality | Yes | Yes | Scoped | Read scoped | Read limited | Read scoped | Read |
| Submit Maintenance Request | Yes | Yes | Yes | Yes | Yes | No | No |
| View requests | All | All scoped | All scoped | Assigned/related | Own | No | Read |
| Triage/approve/convert requests | Yes | Yes | Yes | No | No | No | No |
| Create/plan/schedule/prioritize Work Orders | Yes | Yes | Yes | No | No | No | Read |
| Assign/reassign Work Orders | Yes | Yes | Yes | Optional team claim only | No | No | Read |
| Execute assigned Work Order/checklist/labor | Optional | Yes | Yes | Assigned/team scope | No | Parts only | Read |
| Mark execution Completed | Yes | Yes | Yes | Assigned scope | No | No | Read |
| Close or reopen | Yes | Yes | Scoped | No | No | No | Read |
| Manage preventive plans | Yes | Yes | Scoped | Read | No | No | Read |
| Issue/return stock and see unit costs | Yes | Yes | Policy-based | Request/use; cost hidden by default | No | Yes | Read |
| View/export audit and full history | Yes | Yes | Scoped | Own work history | Own requests | Inventory scope | Yes |

“Yes” still means within tenant and resource scope. Separate `workorder.complete` from `workorder.close`, `workorder.reopen`, `workorder.assign`, `asset.change_criticality`, `inventory.issue`, `cost.view`, and `audit.read` permissions. Technicians should not gain cost visibility or closure authority merely from assignment.

Authentication should use a proven OIDC/OAuth2 provider with ASP.NET Core authentication; do not invent password/token protocols. On every request, derive current organization membership server-side and authorize the loaded resource. Do not trust an `organization_id`, role, assignee, status, cost, creator, or audit field supplied in a request DTO.

## 7. Backend-focused threat model

| Threat | Required control |
|---|---|
| Authz bypass / endpoint omitted policy | Default-deny authorization; named permissions at application-command/query boundary, not only controller attributes; automated endpoint/policy coverage tests |
| IDOR / cross-tenant object reference | Scope every lookup by authenticated organization and permitted site/team; tenant-safe composite FKs; return non-disclosing not-found/forbidden behavior consistently |
| Mass assignment / overposting | Command-specific allow-listed DTOs; map fields explicitly; server sets tenant, actor, state, ownership, costs, audit fields, and generated IDs |
| QR/tag treated as capability | QR resolves only an opaque asset locator; user must still authenticate and authorize. If public fault intake is required, create a separate narrow, rate-limited, revocable token that permits submission only and reveals no asset history |
| Role/scope escalation | Membership and grants changed only through privileged commands; invalidate/shorten stale claims; re-check current membership for high-risk actions; audit all changes |
| Race-based rule bypass | Database uniqueness/conditional writes and root-lock protocol described above; test with genuinely concurrent transactions |
| Duplicate/replayed commands | Scoped idempotency plus request hash at external retry boundaries; natural uniqueness for plan occurrences; never treat a key as authorization |
| Malicious attachments | Direct-to-object-store quarantine, allow-list size/type, content sniffing, hash, malware scan, server-generated key, short-lived authorized download URL, safe content disposition |
| Audit tampering or secret leakage | Application DB role cannot update/delete audit rows; restricted reader role; redact tokens/secrets and minimize personal data; retention/export policy |
| Worker privilege abuse | Worker uses its own least-privilege identity; signed/trusted queue/outbox input; handlers still enforce tenant and invariant checks |
| Query injection / unsafe filtering | Parameterized EF Core queries; allow-list sortable/filterable fields; prohibit raw user-supplied SQL and bound pagination/export sizes |

Rate-limit login, public request/QR resolution, uploads, and expensive exports. Use correlation IDs, structured security logs, and alerting for repeated cross-tenant misses, role changes, bulk exports, and attachment failures.

## 8. Audit trail and Asset History

Create an append-only `audit.audit_events` table with at least:

- `event_id`, `organization_id`, `occurred_at`, `actor_type`, `actor_user_id`/service identity.
- `action`, `resource_type`, `resource_id`, optional parent Work Order/Asset IDs.
- `correlation_id`, `causation_id`, client command/idempotency reference where applicable.
- Explicit `reason` for cancellation, hold, close override, reopen, criticality, and privileged correction.
- Selective structured `before`/`after` values or a typed payload schema version. Do not dump whole EF entities, secrets, tokens, or attachment content.

Write the semantic audit event in the same transaction as the domain change. Domain/application commands—not a generic EF interceptor alone—must name meaningful actions. An interceptor may add low-level metadata but cannot explain intent. The runtime write role gets `INSERT`/`SELECT` only on audit data; no `UPDATE`/`DELETE`. Define retention, partitioning, archival, and regulated export later based on customer obligations.

Audit at minimum:

- Work Order created; lifecycle transition; hold/resume; completion; close; cancel; reopen, including prior/new state and reason.
- Primary/collaborator assignment, unassignment, reassignment, schedule, and priority changes.
- Completion evidence and supervisor verification, with hashes/references rather than copied attachment bodies.
- Preventive plan creation/change/pause/resume, schedule rule/timezone, checklist/template version, and generation occurrence/result.
- Asset identity/location/status and especially criticality changes.
- Part issue/return/reversal, quantity, unit-cost snapshot/currency, and privileged cost corrections; cost-view/export may go to a higher-volume security access log.
- Attachment upload/finalize/link/unlink/quarantine/scan result and privileged download; record object/hash metadata, not signed URLs.
- Membership, role, and scope changes.

Asset History is a read projection ordered by authoritative event time/ID, combining relevant request, Work Order, downtime, preventive, status, and critical audit events. It may be rebuilt and is not itself edited. Display late-arriving events deterministically and retain links to the source records.

## 9. Verification expectations for M1 implementation

Use integration tests against real PostgreSQL with two independent connections/`DbContext`s and synchronization barriers. Minimum concurrency proofs:

1. Two claims: exactly one primary assignee and one assignment audit event.
2. Two completions: exactly one transition/audit event; no duplicated completion side effects.
3. Child edit versus close: edit commits before close or is rejected after it; never changes a closed Work Order.
4. Two scheduler instances plus retry/crash simulation: one occurrence and one preventive Work Order.
5. Reused idempotency key: same payload returns the same result; different payload is rejected.
6. Concurrent part issues: correct stock and ledger totals, no negative quantity, no duplicate posting.
7. Cross-tenant IDs in every write path: rejected without data disclosure.

Also enforce architecture tests so modules cannot write another module's tables/repositories and API DTOs never expose EF entities directly.

## 10. Decisions needed before M1

Keep the next discovery step limited to these product decisions:

1. Confirm tenancy and asset-code uniqueness: organization-wide or site-local.
2. Confirm whether QR intake is authenticated-only or supports a separate public, submit-only fault-report flow.
3. Confirm whether a technician may self-claim team work and whether multiple active assignees need one designated primary.
4. Confirm inventory scope and costing method for the first release (record-only, stock-controlled; standard, moving-average, or another method).
5. Confirm scheduling semantics: site timezone/DST, fixed-calendar versus after-completion recurrence, and whether meter-based plans are in the first release.
6. Confirm who may complete versus close, and whether `Completed` requires all costs posted or costs may settle before `Closed`.

These choices affect constraints and permission policy. Other refinements can remain configurable or deferred without destabilizing the proposed boundaries.
