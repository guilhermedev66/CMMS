# CMMS — Product Vision

## What this is

A Computerized Maintenance Management System (CMMS) for industrial operations:
assets, corrective and preventive maintenance, work orders, technician
execution, and the reporting that plant/maintenance managers actually use to
run their operation (backlog, MTBF/MTTR, availability, cost).

This is not an admin-CRUD demo. It's built to look and behave like a product a
maintenance team would actually adopt: opinionated workflows, real invariants,
and an operator-grade UI (desktop for planners/managers, mobile-capable for
technicians on the floor).

Explicitly not repeating the shape of a prior project on this account
(Fluxora, an ERP/sales system) — different domain, different lifecycle
(Work Order state machine vs. commercial documents), different concurrency
risks (concurrent WO claims, scheduler races) and a mobile/QR dimension
Fluxora didn't have.

## Personas

- **Maintenance Manager / Planner** — owns the maintenance backlog, plans and
  assigns work orders, defines preventive maintenance plans, watches KPIs
  (MTBF, MTTR, availability, cost, backlog). Desktop-first, dense tables,
  filters, calendar view.
- **Technician** — executes work orders on the shop floor. Needs a
  mobile/tablet-first flow: scan QR on an asset, see/​start the relevant work
  order, run the checklist, log parts/downtime/notes/evidence, complete.
  Low-friction, large touch targets, works with intermittent connectivity in
  mind (not full offline-first for v1 — out of scope, see below).
- **Requester** (any employee who spots a problem) — can open a maintenance
  request against an asset without needing full system access.
- **Admin** — manages users, roles, assets/locations, maintenance plans,
  system configuration.

## Scope (v1)

- Assets + location/asset hierarchy
- Maintenance requests (corrective, ad hoc)
- Work orders: full lifecycle, assignment, priority, checklist, parts/costs
  (lean scope — not full inventory), downtime tracking
- Preventive maintenance: plans, recurrence, scheduler, auto-generated work
  orders
- Asset history (audit trail of everything that happened to an asset)
- QR code per asset for fast navigation (QR identifies, never authorizes)
- RBAC with a real permission matrix
- Audit log for lifecycle-significant actions
- Operational dashboard + KPI reporting (MTBF, MTTR, availability, backlog,
  cost, preventive-vs-corrective ratio)
- Light/Dark/System theming from the foundation
- Responsive: desktop admin experience + mobile-capable technician flow

## Out of scope (v1) — see docs/06-milestones.md for the authoritative list

Payroll/HR, accounting, full purchasing/ERP, advanced multi-warehouse
inventory, real IoT/PLC integration, predictive/AI maintenance, digital
twins, multi-tenant SaaS billing, Kafka, Kubernetes, microservices,
offline-first mobile. Complexity has to be earned by an actual requirement,
not added speculatively.

## Non-negotiables

- Security is architecture, not a final pass — threat modeling starts in M0.
- Business invariants (no double-claiming a work order, no duplicate
  preventive job execution, no editing during closure, etc.) are enforced by
  the database (constraints/transactions), not only by C# `if` statements.
- Audit trail for lifecycle-significant actions is tamper-resistant, not an
  afterthought log line.
- Light/Dark/System is foundational, not a final-week retrofit.
- KPI formulas must be defensible — cite the definition, don't invent one.
