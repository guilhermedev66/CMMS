# Architecture Decisions

Condensed ADR log for M0. Full reasoning lives in
`docs/discovery/backend-m0-draft.md` and
`docs/discovery/antigravity-m0-research.md`; this file is the authoritative
summary after descoping (see `docs/01-domain-and-workflows.md` and
`docs/02-security-and-invariants.md` for what changed and why).

This design went through one full adversarial pass — Codex QA's
`docs/qa/m0-adversarial-review.md` (6 BLOCKER / 7 IMPORTANT / 4 OPTIONAL
findings) — before M0 was approved. Every finding has an explicit
resolution inline in `01-domain-and-workflows.md` / `02-security-and-
invariants.md` (search for "QA finding" / "B-0"/"I-0"/"O-0" in those files);
the table below reflects the *resolved* design, not the original draft QA
reviewed.

| ID | Decision | Rationale |
|---|---|---|
| ADR-01 | Modular monolith: one API host, one Worker host, one PostgreSQL database, schema-per-module | Microservices/Kafka/K8s are explicitly out of scope; a monolith with clean module boundaries gets the same domain isolation without the operational overhead a two-person portfolio project can't justify |
| ADR-02 | Request → Work Order is a hard two-stage split; conversion by Planner/Supervisor is the approval | Stops unmoderated backlog spam without adding a second approval gate |
| ADR-03 | Location tree + Asset entity (no ISA-95 7-level hierarchy, no temporal installation ledger) | Real hierarchy without enterprise-scale modeling this project doesn't need |
| ADR-04 | 8-ish state Work Order machine with explicit hold sub-reasons, encoded as a domain-layer transition table with guard predicates | Enough to express real operational blockers without the cognitive overload of a 20-state machine |
| ADR-05 | Calendar-only (Fixed + Floating) preventive recurrence for v1; `RecurrenceType` field reserved for meter-based later | Meter/condition-based needs telemetry ingestion this project doesn't have; field left open so it's not a breaking change later |
| ADR-06 | KPI formulas per SMRP/ISO 14224/EN 13306 definitions, computed on demand from raw stored timestamps, never pre-aggregated | "Mathematically defensible" is a hard requirement in the brief; naive calendar-time-divided-by-count formulas are wrong |
| ADR-07 | PostgreSQL constraints + conditional atomic writes + root-row locking are the correctness authority for every invariant in `02-security-and-invariants.md`; C# owns semantic transitions and policy | Business integrity must survive concurrent requests, not just single-threaded happy paths |
| ADR-08 | Idempotency only at genuine retry boundaries, keyed by `(operation, principal, effective_site, key)` (not global), record + mutation in one transaction, re-authorized on every replay | Matches the brief's explicit instruction; a global key namespace or a trust-once-issued key would both be real IDOR/replay gaps (QA B-06) |
| ADR-09 | RBAC: 4 roles (Admin, Planner, Technician, Requester), role membership per `(user, site)`, permissions as one atomic operation → scope → predicate row each, never a grouped capability row | Lean role set matching the product's actual personas; atomic per-operation rows are what's actually implementable as endpoint policies without ambiguity (QA B-01) |
| ADR-10 | QR codes are opaque UUIDv7 locators, described as anti-guessing not a secret; v1 reveals nothing pre-authentication; a sandboxed public "report an issue" flow (if built) uses a separate rotatable public token, not the internal locator — optional M4 stretch, not required for DoD | Directly answers the brief's QR-enumeration concern without overclaiming what an opaque ID actually protects against (QA I-04, O-01) |
| ADR-11 | Attachments narrowed to raster evidence photos for v1 (manuals/PDF deferred); presigned upload to a server-generated quarantine key, re-authorized+verified at finalize, re-encoded into a separate non-client-writable clean key that alone can go `Active`; signed downloads carry a max TTL and re-authorize at issuance; AV scanning optional/M6 | Closes the realistic upload attack surface by construction (mandatory re-encode) rather than by format-sniffing alone, and removes the validate/use race a shared object key created (QA B-05, I-03, I-05) |
| ADR-12 | Quartz.NET with an ADO.NET PostgreSQL-backed job store; generation locks the `MaintenancePlan` row itself and tracks a single `active_occurrence_id` per plan (not just the per-date unique constraint) | Protects "at most one open generated order per plan" across different due dates and against a redeploy restarting the worker mid-run, not only the single-date duplicate case (QA B-04) |
| ADR-13 | Every Work Order carries `execution_cycle`; `Reopen` increments it rather than overwriting prior completion/closure facts; the WO lifecycle is a full source→target→actor→guard→effect transition table, not a diagram | Makes `Reopen` safe without erasing history, and gives endpoints/Kanban/job handlers one unambiguous contract instead of each inferring legal transitions (QA B-03) |
| ADR-14 | Every security-relevant table's `site_id` is frozen at creation; no cross-site transfer of Assets/Locations/Requests/Work Orders/Plans in v1; child records are authorized only through their owning aggregate root, never by direct child-ID lookup | Removes B-02's actual attack surface (authorization derived from a mutable parent) by cutting the feature that caused it, consistent with this project's general scope-control principle (QA B-02) |
| ADR-15 | Cookie-based session auth (`HttpOnly`/`Secure`/`SameSite`), not a browser-held bearer JWT; anti-forgery on state-changing endpoints; same-origin cookie for the PWA | Removes token-storage/XSS-exfiltration ambiguity that "JWT or cookie" left open (QA I-06) |
| ADR-16 | OpenTelemetry adopted from M1 (traces/metrics via `System.Diagnostics.Activity`/`ILogger`) | Multi-step async flows (QR scan → API → recursive location query; upload → validation worker; scheduler sweep → batch generation) are hard to debug from raw logs alone; native .NET support makes this near-zero-cost to add early rather than retrofit |
| ADR-17 | SignalR adopted, but bounded to the M5 dispatch board (live Work Order board updates, emergency high-priority alerts), with server-derived site-filtered group membership | Full-duplex sockets for every screen would be technology bingo; the dispatch board is the one screen where a stale view causes a real operational problem (double-dispatch) |
| ADR-18 | Deployment: frontend → Vercel, backend → Render, database → Neon PostgreSQL, object storage → Cloudflare R2 | Per brief defaults; no paid tier/trial without explicit authorization |

## Solution layout (guidance for M1, not created yet)

```
Cmms.sln
src/
  Cmms.Api/              HTTP endpoints, authn, policy wiring, composition root
  Cmms.Worker/           Quartz scheduler, outbox dispatch, projections
  Cmms.BuildingBlocks/   IDs, clock, result/error types, transaction & outbox primitives
  Modules/
    IdentityAccess/      users, sites, memberships, roles/permissions
    Assets/              locations, asset registry, criticality
    MaintenanceRequests/ intake, conversion
    WorkManagement/      work orders, assignment, checklist execution, downtime
    PreventiveMaintenance/ plans, schedule calculation, occurrence generation
    PartsAndCosts/        lean part-usage ledger
    Files/                attachment metadata + object-store access
    Audit/                append-only audit event store, history projection
tests/
  Unit/
  Integration/           real PostgreSQL (Testcontainers); constraint & race proofs
  Architecture/           module-boundary dependency tests
docs/
```

One assembly per module (`Domain`/`Application`/`Infrastructure` folders
inside); split into separate assemblies only if build/dependency pressure
actually forces it. Each module owns its own PostgreSQL schema and EF Core
`DbContext`/migration history — a module writes only its own tables.
Cross-module reads go through small application-layer contracts; anything
that must atomically touch two modules' data (e.g. preventive occurrence +
generated Work Order) shares one explicit transaction rather than reaching
for a message broker to avoid a local transaction.

## Data conventions

`timestamptz` for all instants (UTC internally; each Site carries an IANA
timezone for schedule evaluation and display). `numeric` for money, never
floating point. Every mutable aggregate root carries `row_version` for
optimistic concurrency on top of the pessimistic locking used during the
Work Order transaction protocol. `CHECK` constraints own enum-shaped columns
at the migration level, not just C# enum validation.

## What stays out of v1

Payroll/HR, accounting, purchasing/procurement, stock/warehouse management,
real IoT/PLC integration, predictive/AI maintenance, digital twins,
multi-tenant billing, Kafka, Kubernetes, microservices, full offline-first
mobile, meter/condition-based preventive triggers, malware scanning,
multi-assignee Work Orders. Each requires an actual surfaced need plus
explicit approval before it's added — per the brief's scope-control
instruction, complexity has to be earned by the problem.
