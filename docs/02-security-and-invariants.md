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

## Roles and permissions (v1: four roles, atomic operations)

Decision: **Admin, Planner, Technician, Requester.** The research draft
proposed splitting Maintenance Manager/Planner/Supervisor into separate
roles and adding Inventory Clerk and Auditor. Descoped — this product has
lean parts/costs (no dedicated inventory role needed) and read-only audit
access is a permission grant, not a fifth role. Revisit only if a real
need for finer-grained management roles appears once M2 is in flight.

**Role membership is per `(user, site)`**, not a single global role per
user — a person can be a Technician at Site A and a Planner at Site B if
that's ever a real need. `Admin` is the one exception: it is company-wide
by construction, not tied to any site membership row.

**Resolves QA finding B-01.** The original grouped-capability table looked
authoritative but wasn't directly enforceable: several rows combined
operations with genuinely different actor/scope/resource rules under one
cell (e.g. "Claim / assign / reassign" conflated a technician's narrow
self-claim with a planner's broad reassignment authority), which is exactly
the kind of ambiguity that produces a privilege-escalation bug when two
different endpoints implement "the same row" differently. Replaced below
with one row per atomic operation, each naming its scope and resource
predicate explicitly — this is the table endpoints/policies are actually
implemented against.

| Operation | Scope | Admin | Planner | Technician | Requester |
|---|---|---|---|---|---|
| `users.manage`, `sites.manage` | company-global | Yes | — | — | — |
| `assets.create`, `assets.edit` | site | Yes | Yes (own site) | — | — |
| `assets.criticality.change` | site | Yes | Yes (own site, audited) | — | — |
| `assets.read` | site | Yes | Yes (own site) | Yes (own site, read-only) | Limited (tag/name/location only) |
| `requests.create` | site | Yes | Yes | Yes | Yes |
| `requests.read.own` | own record | Yes | Yes | Yes (own submissions) | Yes (own submissions) |
| `requests.read.all` | site | Yes | Yes (own site) | — | — |
| `requests.convert`, `requests.reject` | site | Yes | Yes (own site) | — | — |
| `requests.cancel.own` | own record, `New` only | Yes | Yes | Yes (own, while `New`) | Yes (own, while `New`) |
| `workorders.create`, `.plan`, `.schedule`, `.prioritize` | site | Yes | Yes (own site) | — | — |
| `workorders.selfclaim` | site, unassigned `Open` only | Yes | Yes (own site) | Yes (own site) | — |
| `workorders.assign`, `.reassign`, `.unassign` | site | Yes | Yes (own site) | — | — |
| `workorders.read.assigned` | own assignment | Yes | Yes | Yes (`assignee_id = self` only) | — |
| `workorders.read.all` | site | Yes | Yes (own site) | — | — |
| `workorders.execute` (checklist, labor, downtime, parts entries) | own assignment | Yes | Yes (own site) | Yes (assigned Work Order only) | — |
| `workorders.complete` | own assignment | Yes | Yes (own site) | Yes (assigned Work Order only) | — |
| `workorders.close`, `.reopen`, `.cancel` | site | Yes | Yes (own site) | — | — |
| `plans.manage` | site | Yes | Yes (own site) | — | — |
| `plans.read` | site | Yes | Yes (own site) | Yes (own site, read-only) | — |
| `costs.view` | site | Yes | Yes (own site) | — (hidden by default) | — |
| `audit.read.own` | own actions | Yes | Yes | Yes (own work history) | Yes (own requests) |
| `audit.read.all`, `audit.export` | site | Yes | Yes (own site) | — | — |
| `attachments.read`, `.write`, `.unlink` | inherited from parent resource | (inherits `workorders.*`/`assets.*` grant on the parent — attachments never carry an independent permission) | | | |

Every authorization check is `permission + site + resource-predicate`,
never permission alone — "site" for `Admin` is implicitly all sites; for
every other role it is the acting user's actual site memberships for the
specific site the target resource belongs to (see
`docs/01-domain-and-workflows.md` § Site-boundness for why that's a fixed,
non-mutating value per resource). A valid asset tag, QR scan, or resource ID
is never by itself proof of authorization — the server always re-checks
membership and role for the *current* request against the resource's own
`site_id`, and re-validates that membership is still active at write-commit
time, not only at request start (resolves QA's I-01 — a membership revoked
mid-request must not let an in-flight write land under stale authority).

**Authentication (resolves QA finding I-06 — "JWT or cookie" left two
patterns with different security requirements interchangeable).** Decision:
**cookie-based session**, not a browser-held bearer JWT — `HttpOnly`,
`Secure`, `SameSite=Lax` session cookie issued by ASP.NET Core Identity (or
via OIDC Authorization Code + PKCE if an external IdP is added later; either
way the browser only ever holds an opaque session cookie, never a token in
`localStorage`/`sessionStorage`, closing the XSS-token-exfiltration path).
Anti-forgery token required on every state-changing endpoint. The mobile PWA
uses the same same-origin cookie session — no separate bearer-token path to
reason about. Server derives current site memberships from the
authenticated session on every request; it never trusts `site_id`, role,
assignee, status, cost, or audit fields supplied in a request body
(mass-assignment defense — command-specific allow-listed DTOs, server sets
ownership/state/generated fields). Session rotation on privilege-relevant
changes, explicit logout invalidation, and membership-change invalidation
(a revoked membership ends the session's authority for that site
immediately, not just on next login) are M1 requirements, not deferred.

## Threat model

**Resolves QA finding I-07** — the original table was framed almost
entirely around ordinary HTTP controller endpoints. The same predicates
(permission + site + resource) apply to every surface that returns or
mutates data, not just CRUD commands:

| Threat | Control |
|---|---|
| Authz bypass (endpoint missing a policy check) | Default-deny authorization; named permissions enforced at the application command/query boundary, not only via controller attributes; automated test asserting every endpoint has an explicit policy *and* that the policy matches the atomic-operation table above (a policy existing is not the same as the *correct* policy existing) |
| IDOR / cross-site object reference | Every lookup scoped by the authenticated user's site memberships against the resource's own frozen `site_id`; composite FKs/keys include `site_id`; not-found and forbidden responses look identical to avoid confirming existence; this applies equally to list/search/export/dashboard/calendar/audit-history *projections*, not only single-resource GETs |
| Mass assignment / overposting | Allow-listed command DTOs per action; server sets site, actor, state, cost, and audit fields — never trusts client-supplied values for these |
| QR/tag treated as a capability | QR resolves an **opaque** locator only (see below); scanning it never grants access beyond what the scanning user's own role/site membership already allows |
| Role/scope escalation | Membership and role changes only via privileged admin commands; audited; high-risk actions re-check current membership at write-commit time rather than trusting a cached claim from request start (QA I-01) |
| Race-based rule bypass | Database uniqueness/conditional writes + root-row-lock protocol (below); proven with genuinely concurrent integration tests, not just asserted |
| Duplicate/replayed commands | Scoped idempotency at true retry boundaries (below); natural uniqueness for preventive occurrences; a key is never itself authorization and is re-authorized against current access on every replay (QA B-06) |
| Malicious attachments | Presigned direct-to-storage upload via a bound upload-intent record, allow-listed type/size, magic-byte content verification, SVG rejected outright, images re-encoded server-side into a client-non-writable "clean" key, short-lived signed download URLs re-authorized at issuance (QA B-05, I-05) |
| Audit tampering | The application's runtime DB role has `INSERT`/`SELECT` only on the audit table — no `UPDATE`/`DELETE` (this bounds tampering by the ordinary runtime role; it is not a claim of tamper-proof/non-repudiable history — see the Audit trail section) |
| Query injection / unsafe filtering | Parameterized EF Core queries only; allow-listed sortable/filterable fields; bounded pagination/export size |
| Background jobs / outbox consumers / projections | Least-privilege service identity, distinct from user-facing API identity; outbox/job handlers deduplicate on a stable message ID because delivery is at-least-once — a redelivered message must not double-apply an effect |
| SignalR (M5 dispatch board) | Group membership and every broadcast are server-derived and site-filtered from the connection's authenticated identity, never from a client-supplied site/group parameter; group membership is revoked immediately on membership change or disconnect |
| Frontend/PWA caching | The service worker does not cache authenticated API responses, attachments, or signed URLs by default; cache is purged on logout/user change (QA I-05) |

Rate-limit login, request-submission, uploads, and exports. Log and alert on
repeated cross-site access misses, role changes, and bulk exports.

## QR strategy

**Decision:** each asset gets an opaque, high-entropy locator (UUIDv7), never
a sequential integer, in its QR URL (`/scan/{uuid}`). **Narrowed per QA
finding O-01:** this makes blind online enumeration impractical — it does
not, by itself, prevent disclosure through a photographed tag, a leaked
screenshot, a log line, or a referrer header, and UUIDv7 deliberately
encodes an approximate creation timestamp. Treat it as an anti-guessing
measure, not a secret or a capability. Because of that: **v1 reveals no
asset data before authentication and authorization** — an unauthenticated
scan redirects straight to login (return-URL validated as a local path
only, resolving the ambiguous "public info might be returned" wording QA
flagged in I-04); after login, the full atomic permission table above
applies with no shortcut.

A sandboxed **public "report an issue"** intake is a well-justified
*optional* M4 stretch feature — it's what makes the "QR never equals
authorization" property concretely demonstrable — but it is not required
for any milestone's Definition of Done. **If it is built** (QA I-04): it
must use a **separate, purpose-bound, rotatable public-report token**, not
the same internal asset locator used for the authenticated deep link (so a
leaked public token can be rotated without reprinting every QR tag in the
building); it exposes only tag/name/location, never history/cost/
schematics; submissions are rate-limited across multiple dimensions (per
token, per source IP, per deployment-wide volume) and moderated/deduped;
and — regardless of whether this stretch feature ships — the post-scan
authenticated Work Order list always applies ordinary `workorders.read.*`
visibility, so asset/site access alone never exposes another technician's
unassigned or unrelated work.

## Attachment strategy

**Decision:** attachments are in scope, but **narrowed for v1 to bounded
raster evidence photos only** (checklist evidence, before/after repair
photos) — equipment manuals/PDF documents are deferred past v1. This is a
direct response to QA finding **I-03**: magic-byte verification proves a
file's *format*, not that it's harmless — a structurally valid PDF can carry
active content, and a document viewer's own parser is out of this
application's control. A raster image that gets unconditionally decoded and
**re-encoded** server-side has no way to smuggle anything through that
re-encode step (the output is bytes this application generated, not bytes
the uploader controlled), which is a real, checkable security boundary
instead of a hopeful one. Manuals/PDFs get reconsidered only alongside
either AV/content-disarm-and-reconstruction or an accepted, explicitly
documented residual risk — not shipped on magic-byte checking alone.

**Resolves QA finding B-05** — the original pipeline let the client's
presigned PUT target the same object key the validation worker then
activated, meaning a bearer-capability URL could be reused to overwrite
already-validated bytes, and finalize wasn't specified to re-authorize or
verify the actual object. Revised pipeline:

1. Client requests an upload: API creates an `AttachmentUploadIntent` row —
   bound to `actor_id`, `site_id`, `parent_resource_type`/`parent_resource_id`,
   a **server-generated random quarantine key**, `max_bytes`, the declared
   allowed type, an expiry (15 minutes), and state `Pending`. The presigned
   PUT URL is scoped to exactly that quarantine key.
2. Client uploads directly to the quarantine key; storage bypasses the API
   server entirely for the byte stream.
3. Client calls finalize. The worker **re-authorizes the actor against the
   parent resource's *current* state** (not just the state at step 1 — the
   parent could have closed/been reassigned meanwhile), verifies the actual
   stored object's size and magic bytes against the intent (not the
   client's original claim), rejects SVG outright, and decodes+re-encodes
   the image (stripping EXIF/GPS in the process).
4. The re-encoded output is written to a **separate "clean" key that the
   client never had write access to**. Only that clean key can ever become
   the attachment's `Active` object — the quarantine key is deleted after
   finalize (or expires unused) and **never has a download route**.
5. Activation, link, and unlink are ordinary Work Order/Asset-root-locked
   mutations, per the concurrency protocol below — not a side-channel write
   the worker performs outside that protocol.

Storage keys are server-generated random values under a site-prefixed path
— never a user-supplied filename. **Downloads (resolves QA I-05):** a
signed URL is authorized through the attachment's *current* parent resource
at issuance time, signs an immutable object version, carries a maximum TTL
(5 minutes) with no "short-lived" left undefined, and sets
`Content-Disposition: attachment` + `X-Content-Type-Options: nosniff`. A
signed URL already issued remains a bearer capability until it expires —
five minutes is the accepted exposure window for v1; immediate hard
revocation (e.g. on membership removal mid-download) would require an
authorizing proxy instead of direct signed storage URLs, which is more
infrastructure than this project's threat model justifies. Documented as an
accepted trade-off, not an oversight.

Object storage: a S3-compatible provider (Cloudflare R2 — S3 API, generous
free tier, pairs cleanly with a Render-hosted API) — confirmed choice for
M4, no trial/paid commitment needed to start.

Decision: **malware/antivirus scanning (e.g. ClamAV) is an optional M6
hardening item**, not required for the M4 Definition of Done. With the
raster-only + mandatory re-encode boundary above, the realistic v1 attack
surface (arbitrary executable upload, SVG XSS, spoofed extension, decoder
exploitation via unbounded input) is closed by construction rather than by
AV signature matching — the validation worker still enforces explicit byte
size, pixel dimension, memory, and CPU/time bounds so a malformed image
can't itself become a resource-exhaustion vector. AV scanning is additional
defense-in-depth to add in M6 if time permits, not the thing this boundary
depends on.

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
| A `PhotoRequired` checklist item's evidence is still validating (async) when completion is attempted (QA I-02) | Attachment activation/link/unlink itself locks the Work Order root, same as any other child mutation; only an `Active` attachment linked to the exact checklist item satisfies the guard — a `Pending` one does not | `Mark Completed` is rejected (guard unmet) until validation finishes and the attachment is `Active`, never a race between "looks done" and "actually done" |
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

**Resolves QA finding B-06** — `(operation_name, key)` alone is a *global*
namespace: two different users (or two different sites) could collide on
the same key, or a replay handler could return a cached result before
re-checking whether the caller is still allowed to see it. Idempotency
records are unique by **`(operation_name, principal_id, effective_site_id,
key)`**, and the stored request hash includes the server-derived principal,
effective site, target resource type, and an operation-schema version — not
just the client's payload. The idempotency record and the domain mutation
commit **in the same transaction** (the same protocol as the rest of this
section — no separate best-effort write after the fact), so a crash can't
leave a completed mutation without its replay record, or vice versa. A
public/anonymous intake path (if the optional QR public-report feature is
ever built) is namespaced instead as `(operation_name, anonymous,
source-identifier, key)` with its own narrower semantics. On **every**
replay — not just the first call — the handler re-authorizes current access
to the resulting resource before returning it: a key must never resurrect
access to something the caller has since lost permission to see.

Reads, deletes of a known resource, ordinary optimistic-concurrency edits,
and the atomic claim above already have stable identity/conditional
semantics and do **not** need an idempotency key — building one for every
command would be exactly the "spread `Idempotency-Key` over common CRUD"
the brief warns against.

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

**Trust boundary, stated explicitly (resolves QA finding O-03):** removing
`UPDATE`/`DELETE` from the runtime role stops the *ordinary* application
path from altering or erasing existing audit rows. It does **not** make the
table tamper-proof or non-repudiable — a fully compromised application
server or database credential can still insert a forged row, and a database
owner/migration role always retains broader access by necessity. This is an
accepted v1 trust boundary, not a claim of cryptographic integrity.
Hash-chaining audit rows or exporting them to an independently-controlled
sink is a legitimate M6 hardening item if there's time for it — not a gap
being silently ignored.

Minimum audited actions: Work Order lifecycle transitions (with prior/new
state + reason), assignment/reassignment, priority changes, completion
evidence + supervisor verification, preventive plan create/change/pause/
resume, asset criticality/location changes, part postings and privileged
cost corrections, attachment upload/link/unlink, membership/role changes.

Asset History (shown on the Asset detail page) is a **read projection**
over Work Orders + audit events, ordered by event time — rebuildable, never
itself directly mutated.
