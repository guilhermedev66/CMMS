# CMMS — Computerized Maintenance Management System

A production-shaped CMMS/EAM (Enterprise Asset Management) backend and frontend: asset registry,
maintenance requests, Work Order lifecycle, preventive maintenance scheduling, technician execution
(checklists/downtime/parts/photo evidence), QR-driven navigation, and an operational reporting
dashboard with live updates. Built solo end-to-end — domain modeling, security design, concurrency
correctness, and frontend — as a demonstration of how a real maintenance operation's rules translate
into a system that won't corrupt itself under concurrent, adversarial, or careless use.

## The problem

Industrial and facilities maintenance teams run on some mix of spreadsheets, paper, and CMMS
software that treats correctness as an afterthought: two technicians can claim the same Work Order,
a QR tag scan can leak more than it should, "when did this asset last fail" turns into a spreadsheet
pivot nobody trusts. This project's actual engineering thesis is narrower and more concrete than "build
a CMMS": **every place two people can race for the same resource, or a scanned tag could be mistaken
for a credential, is a database-level guarantee, not an application-level hope** — proven by genuinely
concurrent integration tests against a real PostgreSQL instance, not asserted in a design doc.

## Architecture

**Modular monolith**, one ASP.NET Core minimal-API host (`Cmms.Api`) composing independent modules,
each owning its own PostgreSQL schema and EF Core `DbContext`:

```
src/
  Cmms.Api/                  minimal-API endpoints, composition root, Program.cs
  Cmms.BuildingBlocks/       cross-module primitives (SharedTransactionScope, schema names)
  Modules/
    IdentityAccess/          users, sites, roles, RBAC permission catalog
    Assets/                  asset + location hierarchy, QR locator
    Audit/                   append-only audit_events (INSERT/SELECT-only DB role)
    MaintenanceRequests/     the intake flow ahead of a Work Order
    WorkManagement/          Work Order lifecycle, checklist/downtime/parts (M4)
    PreventiveMaintenance/   plans, recurrence, background generation
    Attachments/             quarantine -> re-encode -> clean-key upload pipeline
web/                         React 19 + TypeScript + Vite + Tailwind v4 SPA
tests/Cmms.IntegrationTests/ real PostgreSQL (Testcontainers) + real HTTP host per test
```

A module never references another module's project directly — cross-module reads go through the
API layer (e.g. `WorkOrdersEndpoints` reads `AssetsDbContext` to validate a Work Order's asset
belongs to the same site) and cross-module *writes that must be atomic together* share one
ADO.NET connection/transaction via `SharedTransactionScope`, rather than reaching for a message
broker or a distributed saga to avoid a plain local transaction. This is a deliberate, documented
trade-off (`docs/03-architecture-decisions.md`) against splitting into real microservices: one
Postgres instance, one deployable API, and schema boundaries that make a future service extraction
possible without having paid its operational cost up front.

**Stack**: .NET 10 / ASP.NET Core minimal APIs, EF Core + Npgsql (PostgreSQL), ASP.NET Core Identity
(cookie session auth, no bearer JWT — see *Security*), SignalR (bounded to one feature, see
*Reporting*), OpenTelemetry, React 19 + TypeScript + Vite + Tailwind CSS v4, xUnit + Testcontainers.

## Domain model

**Asset hierarchy**: `Location` (recursive tree — Site → Area → Line/Cell) and `Asset` (physical
equipment, optional `ParentAssetId` for sub-components, ABC criticality classification, an opaque
`QrLocator` distinct from its database id). Deliberately two entities, not a full ISA-95 seven-level
tree — that granularity is real for a multi-plant conglomerate, not a portfolio-scoped v1.

**Work Order lifecycle** (`docs/01-domain-and-workflows.md` carries the full normative transition
table): `Draft → Open → Scheduled → InProgress → Completed → Closed`, plus `Cancel` and `Reopen`
(which increments an `execution_cycle` counter — every reopen starts a fresh, independently-tracked
cycle of checklist/downtime/parts data without overwriting the prior cycle's history). Self-claim is
the flagship concurrency case: `Open → Scheduled` is a single atomic conditional `UPDATE ... WHERE
assignee_id IS NULL AND status = 'Open'`, not a read-then-write — see *Concurrency* below.

**Maintenance execution** (M4): checklist items (Boolean/Numeric-with-tolerance/SingleSelect/
PhotoRequired/Note), downtime intervals (`FullStop` vs `PartialDerating`, with a PostgreSQL
exclusion constraint making two overlapping FullStop intervals on the same asset impossible at the
database level, not just checked in application code), and an immutable parts-usage ledger
(record-only, no stock levels — deliberately lean). `Mark Completed` enforces a real guard: every
required checklist item resolved, no open downtime interval, computed under the same root lock the
transition itself takes.

**Preventive maintenance**: `MaintenancePlan` → background sweep generates a `MaintenancePlanOccurrence`
+ Work Order on schedule (`Fixed` recurrence anchored to the calendar; `Floating` recurrence computed
from the *actual* completion time of the prior occurrence). `SuppressIfOpen` prevents a plan from
double-generating while a prior occurrence is still open, enforced via a plan-row lock plus a unique
`(plan_id, scheduled_for)` database index as the final safety net even if the lock protocol is ever
bypassed.

**Attachments**: raster evidence photos only (no PDFs/manuals in v1 — see *Security*), a 5-step
quarantine → re-authorize → verify → decode/re-encode → clean-key pipeline.

## Concurrency & idempotency

Every Work Order-scoped mutation follows one protocol: begin transaction → `SELECT ... FOR UPDATE`
the Work Order root → authorize against site scope and current status → lock any additional shared
rows in a deterministic order → validate and mutate → insert the audit event in the same transaction
→ commit. This is proven, not just documented — genuinely concurrent tests fire real simultaneous
HTTP requests via `Task.WhenAll` against a real running host and a real PostgreSQL instance:

- **Two technicians self-claiming the same Work Order** resolve to exactly one winner (one `200`,
  one `409`, never a `500`, never both/neither assignee set).
- **Two overlapping scheduler sweeps** for the same due preventive plan generate exactly one
  occurrence and one Work Order.
- **Two concurrent downtime-interval opens** on the same asset are rejected by a PostgreSQL
  exclusion constraint, not an application-level check that a race could slip past.
- **Two concurrent attachment-finalize calls** for the same upload intent resolve to exactly one
  persisted attachment, not a duplicate row or an unhandled error.

Idempotency is applied only at genuine at-least-once retry boundaries (preventive occurrence
generation, part-usage postings, attachment finalization) — not spread across ordinary CRUD, per
`docs/02-security-and-invariants.md`'s explicit reasoning against over-applying `Idempotency-Key`.

## Security

- **Cookie-based session auth** (`HttpOnly`/`Secure`/`SameSite=Lax`), not a browser-held bearer
  JWT — removes token-storage/XSS-exfiltration ambiguity. CSRF-protected on every state-changing
  endpoint (double-submit token, `X-CSRF-TOKEN` header).
- **RBAC**: a permission catalog (`permission + site-scope + resource-ownership predicate`) checked
  explicitly at every endpoint — no cached role claim; membership is re-validated against live
  database state on every request, so a revoked membership loses authority on the caller's very
  next request, not at next login. Cross-site and no-permission responses are identical (`404`),
  so a resource id never confirms existence to an unauthorized caller.
- **QR is a locator, never a capability.** A scanned tag resolves through the exact same RBAC path
  as any other lookup by id — there is no separate "QR bypass," proven by a negative test (an
  unauthenticated scan gets a plain `401`; a same-tag scan from a different site's technician gets
  the same `404` a cross-site lookup would).
- **Attachments**: server-generated quarantine/clean storage keys (a client never controls a
  filesystem path), magic-byte verification via mandatory image decode (ImageSharp) rather than a
  spoofable extension/content-type check, SVG rejected by construction (no SVG decoder exists to
  fool), EXIF/GPS stripped on re-encode, decompression-bomb bounds on pixel dimensions checked
  *before* full decode, and every finalize re-authorizes the actor against the parent Work Order's
  *current* state (not the state when the upload started).
- **Rate limiting**: a global per-user/per-IP sliding-window budget on every endpoint, a tighter
  dedicated policy on login (credential-guessing), and a dedicated policy on attachment uploads
  (bandwidth abuse).
- **Response hardening**: `X-Content-Type-Options`, `X-Frame-Options: DENY`, a `default-src 'none'`
  CSP (this API only ever serves JSON/files to its own SPA, never renders HTML), HSTS when secure
  cookies are required.

## Observability

OpenTelemetry (traces + metrics) on ASP.NET Core request handling, outbound HTTP calls, and
PostgreSQL queries (via Npgsql's native `ActivitySource` — no separate EF instrumentation package
needed). A console exporter runs by default (visible in `docker compose logs`/any hosting
provider's log stream with zero setup); an OTLP exporter activates only if `Otel:OtlpEndpoint` is
configured, so this is never "on" only because a specific vendor was signed up for.

## Reporting & live dispatch board

`GET /reports/kpis` computes MTBF, MTTR, MDT, Operational/Inherent Availability, Planned
Maintenance %, preventive-vs-corrective split, parts cost, backlog, and overdue-preventive counts —
every formula cited to its source (SMRP Best Practice Guide / ISO 14224 / EN 13306, via
`docs/01-domain-and-workflows.md`), recomputed from raw transactional rows on every call (nothing
is a persisted, driftable pre-computed average). A metric is `null` — never `0` or `Infinity` — when
its underlying population is empty (zero failures in the window is good news, not a zero-hour MTBF).
MTBF/MTTR/MDT/Inherent Availability are reported only for a specifically-selected asset: averaging
them across a site's whole heterogeneous asset mix isn't mathematically defensible, so the API
refuses to fabricate a blended number.

A SignalR hub (`/hubs/work-orders`) drives a live dispatch board — group membership is derived
entirely server-side from the connected user's own site memberships (never a client-supplied site
parameter), proven by two real concurrent SignalR connections in a test asserting a Site A broadcast
never reaches a Site B connection.

## Testing

46 integration tests, all driving a real HTTP host against a real PostgreSQL instance
(Testcontainers) — no mocked database, no simulated concurrency. Covers: the full RBAC/IDOR matrix
across every module, every documented concurrency race, attachment pipeline security (path
traversal, oversized/wrong-type rejection, the finalize race), KPI reconciliation against
independently-recomputed raw-row queries, rate-limiting behavior, and SignalR group isolation.
Frontend: Vitest + Testing Library component/integration tests, `oxlint`, and a full `tsc -b` +
`vite build` typecheck/build gate. CI (`.github/workflows/ci.yml`) runs all of the above plus a
Docker image build, on every push to `main` and every PR.

```bash
dotnet test Cmms.sln                 # backend — needs Docker for Testcontainers' PostgreSQL
cd web && npm run lint && npm run build && npm run test
```

## Running locally

```bash
cp .env.example .env      # adjust ports/passwords if you want
docker compose up
```

Boots PostgreSQL + the API, applies migrations, and bootstraps an admin account (from
`BootstrapAdmin__Email`/`BootstrapAdmin__Password` in `.env`, defaults in `.env.example`) — API on
`http://localhost:8080`, health check at `/health`. For the frontend:

```bash
cd web && npm install && npm run dev
```

Vite's dev server proxies `/api/*` to `http://localhost:8080` (see `web/vite.config.ts`) so the
browser only ever talks to one origin — the same same-origin-cookie architecture production uses
(see *Deployment*).

## Deployment

Target infrastructure (`docs/03-architecture-decisions.md`, ADR-18): **Vercel** (frontend, static
build) + **Render** (backend, Docker) + **Neon** (PostgreSQL, serverless). `render.yaml` (repo root)
and `web/vercel.json` are the live configs — the Vercel rewrite proxies `/api/*` to the Render
backend (SignalR's `/hubs/work-orders` included, since the frontend always dials it through
`/api/hubs/work-orders`) so the browser stays same-origin in production exactly as it does in dev,
which is what lets session cookies work without CORS.

Current state:

- **Frontend** — live on Vercel: https://cmms-web-mocha.vercel.app
- **Database** — provisioned on Neon (`cmms-production`, `aws-us-east-2`, Postgres 18)
- **Backend** — service created on Render (`cmms-api`, Docker runtime,
  https://cmms-api-ev0z.onrender.com) but not yet serving traffic: its `/health` endpoint checks
  real DB connectivity (`Program.cs`), so it correctly refuses to go live until
  `ConnectionStrings__Cmms` and the bootstrap-admin env vars are set on the service — the one step
  requiring a human in Render's dashboard (Claude Code's own credential-handling guardrails won't let
  an agent transmit a freshly-provisioned DB connection string or generated password through a CLI
  command, by design). `.github/workflows/smoke.yml` (`workflow_dispatch`) verifies API health, the
  frontend, and the `/api` rewrite end-to-end from GitHub's network once that's done.

**Known limitation, disclosed rather than hidden**: `LocalDiskAttachmentStorage` (the dev/CI
substitute for a presigned Cloudflare R2 upload — see `AttachmentUploadIntent`'s doc comment for the
full rationale and the security properties preserved either way) writes to local disk. On Render's
free tier that disk isn't persistent, so uploaded evidence photos won't survive a redeploy. Fine for
a demo; swapping `IAttachmentStorage` for a real R2-backed implementation is a drop-in change behind
that interface, not a redesign, and is the one item between this deployment and a genuinely durable
production one.

## Trade-offs and scope cuts (named, not hidden)

This project follows one rule throughout its own documentation: **when the actual schema or a
bounded milestone slice doesn't support a textbook formula or a "complete" feature, that's stated
explicitly at the point it's made, never silently approximated.** A sample of the load-bearing ones:

- No crew/staffing entity exists, so "Backlog" is reported as a live open-order count, not the
  textbook crew-weeks figure (`Σ Estimated Labor Hours / Available Craft Hours`).
- No per-Work-Order labor-hours ledger exists; Planned Maintenance % uses each order's wrench-time
  span as a labor-hours proxy — the same scope cut carried consistently from M4 through M5's
  reporting.
- No asset replacement-value field exists, so `%RAV` (cost as % of replacement value) was not built
  at all, matching the domain doc's own guidance to treat it as a stretch metric.
- Malware/AV scanning on attachments is an accepted, documented M6-optional hardening item, not a
  gap the raster-only + mandatory-re-encode boundary depends on to be safe.
- A public, unauthenticated "report an issue via QR" intake was scoped as optional in M0 and was
  never built — every QR flow in this system requires authentication.

`docs/06-milestones.md` is the authoritative, evidence-cited record of what's PASS, what's pending,
and what's an accepted trade-off for every milestone — including two BLOCKER/IMPORTANT findings a
fresh independent adversarial review caught and this repo's history shows fixed with regression
tests, not just claimed fixed.

## Documentation index

- `docs/00-product-vision.md` — scope, personas, what's explicitly out of scope
- `docs/01-domain-and-workflows.md` — full domain rules, transition tables, KPI formulas
- `docs/02-security-and-invariants.md` — permission matrix, threat model, concurrency protocol
- `docs/03-architecture-decisions.md` — every ADR, with its rejected alternatives
- `docs/04-frontend-ia.md` — information architecture, visual direction, mobile strategy
- `docs/06-milestones.md` — milestone-by-milestone Definition of Done, evidence, and status
