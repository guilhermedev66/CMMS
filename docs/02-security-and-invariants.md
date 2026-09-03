# Security, RBAC & Business Invariants

Synthesized from `docs/discovery/backend-m0-draft.md` (RBAC/threat model/
concurrency proposal) and `docs/discovery/antigravity-m0-research.md`
(QR and attachment security sections), with the multi-tenant framing removed
per `docs/01-domain-and-workflows.md` — this is a single-company, multi-site
product, so "tenant isolation" becomes "site-scoped authorization," and the
IDOR concern is cross-site, not cross-customer.

Security is architecture, decided in M0 — not a final-milestone hardening
pass. Every invariant below is enforced by PostgreSQL (constraint,
conditional write, or transaction), not only by a C# `if`.

## Roles (v1: four, not seven)

Decision: **Admin, Planner, Technician, Requester.** The research draft
proposed splitting Maintenance Manager/Planner/Supervisor into separate
roles and adding Inventory Clerk and Auditor. Descoped — this product has
lean parts/costs (no dedicated inventory role needed) and read-only audit
access is a permission grant, not a fifth role. Revisit only if a real
need for finer-grained management roles appears once M2 is in flight.

Admin is company-wide by definition — it is the one role not bound by site
membership, which is why "Yes" for Admin means all sites throughout the
table below (equivalent to the explicit "All sites" written on the Requests
row). Every other role's "Yes"/"Scoped to site" is always evaluated against
the acting user's actual site memberships — there is no implicit
company-wide grant for Planner/Technician/Requester.

| Capability | Admin | Planner | Technician | Requester |
|---|---:|---:|---:|---:|
| Manage users, roles, sites, settings | Yes | No | No | No |
| Create/edit assets, change criticality | Yes | Scoped to site | Read (scoped) | Read (limited) |
| Submit Maintenance Request | Yes | Yes | Yes | Yes |
| View / convert / reject Requests | All sites | Scoped to site | Own submissions | Own submissions |
| Create / plan / schedule / prioritize Work Orders | Yes | Scoped to site | No | No |
| Claim / assign / reassign Work Orders | Yes | Scoped to site | Self-claim unassigned, own site | No |
| Execute checklist / log labor, downtime, parts | Yes | Yes | Assigned Work Order only | No |
| Mark Completed | Yes | Yes | Assigned Work Order only | No |
| Close / Reopen | Yes | Scoped to site | No | No |
| Manage preventive plans | Yes | Scoped to site | Read only | No |
| View costs | Yes | Scoped to site | No (hidden by default) | No |
| View/export audit log | Yes | Scoped to site | Own work history only | Own requests only |

"Scoped to site" means: the grant applies only within the sites the user is
a member of. Every authorization check is `permission + site + resource`,
never permission alone. A valid asset tag, QR scan, or resource ID is never
by itself proof of authorization — the server always re-checks membership
and role for the *current* request.

Authentication: a proven OIDC/OAuth2-shaped flow via ASP.NET Core Identity +
JWT (or cookie) — no hand-rolled password/token protocol. Server derives
current site memberships from the authenticated identity on every request;
it never trusts `site_id`, role, assignee, status, cost, or audit fields
supplied in a request body (mass-assignment defense — command-specific
allow-listed DTOs, server sets ownership/state/generated fields).

## Threat model

| Threat | Control |
|---|---|
| Authz bypass (endpoint missing a policy check) | Default-deny authorization; named permissions enforced at the application command/query boundary, not only via controller attributes; automated test asserting every endpoint has an explicit policy |
| IDOR / cross-site object reference | Every lookup scoped by the authenticated user's site memberships; composite FKs/keys include `site_id`; not-found and forbidden responses look identical to avoid confirming existence |
| Mass assignment / overposting | Allow-listed command DTOs per action; server sets tenant/site, actor, state, cost, and audit fields — never trusts client-supplied values for these |
| QR/tag treated as a capability | QR resolves an **opaque** locator only (see below); scanning it never grants access beyond what the scanning user's own role/site membership already allows |
| Role/scope escalation | Membership and role changes only via privileged admin commands; audited; high-risk actions re-check current membership rather than trusting a cached claim |
| Race-based rule bypass | Database uniqueness/conditional writes + root-row-lock protocol (below); proven with genuinely concurrent integration tests, not just asserted |
| Duplicate/replayed commands | Scoped idempotency at true retry boundaries (below); natural uniqueness for preventive occurrences; a key is never itself authorization |
| Malicious attachments | Presigned direct-to-storage upload, allow-listed type/size, magic-byte content verification, SVG rejected outright, images re-encoded server-side, random server-generated storage keys, short-lived signed download URLs |
| Audit tampering | The application's runtime DB role has `INSERT`/`SELECT` only on the audit table — no `UPDATE`/`DELETE` |
| Query injection / unsafe filtering | Parameterized EF Core queries only; allow-listed sortable/filterable fields; bounded pagination/export size |

Rate-limit login, request-submission, uploads, and exports. Log and alert on
repeated cross-site access misses, role changes, and bulk exports.

## QR strategy

**Decision:** each asset gets an opaque, high-entropy locator (UUIDv7), never
a sequential integer, in its QR URL (`/scan/{uuid}`). This alone defeats
enumeration regardless of what happens after the scan. Possessing the QR
(or guessing/copying the URL) is never treated as proof of authorization —
the server always re-checks the *scanning user's* role and site membership
before returning anything beyond the asset's public tag/name/location.

For v1, an unauthenticated scan redirects to login (return-URL preserved);
after login, normal RBAC applies. A sandboxed **public "report an issue"**
intake (unauthenticated scan → minimal public asset info + a single
rate-limited "submit request" action, no history/cost/schematics exposed) is
a well-justified *optional* M4 stretch feature — it's what makes the "QR
never equals authorization" property concretely demonstrable — but it is
not required for any milestone's Definition of Done, so it doesn't block
M4 if time-constrained. Either way, the ID scheme (opaque UUIDv7) is decided
now so adding the public flow later never requires a breaking migration.

## Attachment strategy

**Decision:** attachments are in scope (evidence photos, equipment manuals),
using presigned direct-to-object-storage uploads — the API server never
buffers raw file bytes. Pipeline: client requests a presigned PUT URL (API
validates size/type policy first) → client uploads directly to storage →
client confirms → an async worker verifies true file type via magic bytes,
rejects anything that doesn't match, **rejects SVG outright** (stored XSS
risk), re-encodes raster images (also strips EXIF/GPS), and only then marks
the attachment `Active`. Storage keys are server-generated random values
under a site-prefixed path — never a user-supplied filename. Downloads use
short-lived signed URLs with `Content-Disposition: attachment` and
`X-Content-Type-Options: nosniff`.

Object storage: a S3-compatible provider (Cloudflare R2 — S3 API, generous
free tier, pairs cleanly with a Render-hosted API) — confirmed choice for
M4, no trial/paid commitment needed to start.

Decision: **malware/antivirus scanning (e.g. ClamAV) is an optional M6
hardening item**, not required for the M4 Definition of Done — running a
scanning daemon is real infrastructure weight for a portfolio deployment.
Magic-byte verification + type allow-list + image re-encoding already closes
the realistic attack surface (arbitrary executable upload, SVG XSS,
spoofed extension) for this project's threat model; add AV scanning only if
there's time in M6 without it blocking closure.

## Concurrency & invariants

Every Work Order-scoped mutation follows one protocol: begin transaction →
`SELECT ... FOR UPDATE` the Work Order root → authorize against site scope
and current status → lock any additional shared rows in a deterministic
order → validate and mutate → insert the audit event + outbox message in the
same transaction → increment `row_version` → commit. Keep transactions
short; never call storage/email/other network services while holding a lock
(commit an outbox message, act on it afterward).

| Race | Correctness mechanism | Outcome |
|---|---|---|
| Two technicians claim the same Work Order | Atomic conditional `UPDATE ... WHERE assignee IS NULL AND status IN (...)` — not read-then-write | Exactly one row updates; the loser gets zero rows affected, not a 500 |
| Two concurrent completions | Root row lock + expected-status/`row_version` check | One transition + one completion event; the loser sees "already completed" |
| Child edit (checklist/parts/downtime) races with completion/closure | Both commands lock the Work Order root before touching child data | Edit lands entirely before completion, or is rejected after — never a partial closure |
| A preventive job fires twice | Unique `(plan_id, scheduled_for)` occurrence + atomic generation transaction | Exactly one occurrence and one Work Order, regardless of scheduler timing |
| Two scheduler ticks/instances (including a redeploy restarting the worker mid-run) | `FOR UPDATE SKIP LOCKED` claim of due plans + the uniqueness constraint above as the real safety net | Work is split across claimers; a crash/retry cannot duplicate output |
| Retry after an ambiguous Work Order creation (client got no response) | Source-request uniqueness, or a scoped idempotency record for direct creation | Same request returns the prior result; a different payload under the same key is rejected |
| Concurrent part usage postings | Work Order editability check, then an immutable insert into the part-usage ledger (no stock row to lock — v1 is record-only, no stock levels); a client-supplied idempotency key deduplicates a retried insert | No duplicate posting; correct total from summing ledger rows, not a mutable counter |

The claim example, concretely — this is the invariant the brief calls out
explicitly ("duas pessoas tentando assumir a mesma OS"):
```sql
UPDATE work_orders
SET assignee_id = :user_id, assigned_at = now(), row_version = row_version + 1
WHERE id = :work_order_id
  AND site_id = :site_id
  AND assignee_id IS NULL
  AND status IN ('Open', 'Scheduled')
RETURNING id, row_version;
```
Zero rows returned means "already claimed or no longer claimable" — an
expected outcome the application maps to `409 Conflict`, not an error.

## Idempotency — where it's actually justified

Not spread across ordinary CRUD. Applied only at genuine at-least-once retry
boundaries:

- Public/mobile creation of Requests or direct Work Orders.
- Preventive occurrence generation — using the natural `(plan_id,
  scheduled_for)` key, not a generic random idempotency key.
- Inventory/part-usage postings.
- Attachment upload finalization, if a client can plausibly retry it.

Idempotency records are unique by `(operation_name, key)`, store a request
hash and the resulting resource ID, and reject reuse of the same key with a
different payload. A key is never itself proof of authorization. Reads,
deletes of a known resource, ordinary optimistic-concurrency edits, and the
atomic claim above already have stable identity/conditional semantics and
do **not** need an idempotency key — building one for every command would be
exactly the "spread `Idempotency-Key` over common CRUD" the brief warns
against.

## Audit trail

Append-only `audit_events` table: `event_id`, `occurred_at`, `actor_user_id`
(or service identity), `action`, `resource_type`/`resource_id`, `site_id`,
`correlation_id`, an explicit `reason` for cancellation/hold/close-override/
reopen/criticality-change/privileged correction, and a selective
before/after payload (never a full entity dump, never secrets/attachment
content). Written in the *same transaction* as the domain change, by the
domain command itself — not bolted on afterward by a generic interceptor,
which can log a field diff but can't name the business intent. The
application's DB role can `INSERT`/`SELECT` but never `UPDATE`/`DELETE` this
table.

Minimum audited actions: Work Order lifecycle transitions (with prior/new
state + reason), assignment/reassignment, priority changes, completion
evidence + supervisor verification, preventive plan create/change/pause/
resume, asset criticality/location changes, part postings and privileged
cost corrections, attachment upload/link/unlink, membership/role changes.

Asset History (shown on the Asset detail page) is a **read projection**
over Work Orders + audit events, ordered by event time — rebuildable, never
itself directly mutated.
