# M0 Adversarial Security and Invariant Review

**Review date:** 2026-09-03  
**Scope:**

- `docs/01-domain-and-workflows.md`
- `docs/02-security-and-invariants.md`
- `docs/03-architecture-decisions.md`
- `docs/04-frontend-ia.md`

This is a document-level adversarial review. No implementation exists, so the
items below are design defects or unresolved security requirements rather than
verified code vulnerabilities.

## Verdict

**M0 should not be approved until all BLOCKER findings have explicit,
testable resolutions in the design documents.**

Finding count: **6 BLOCKER / 7 IMPORTANT / 4 OPTIONAL**.

Severity convention:

- **BLOCKER:** the present design can reasonably lead to an authorization,
  state-integrity, or concurrency failure; resolve before approving M0.
- **IMPORTANT:** not necessarily an M0 approval gate by itself, but must be
  specified before the affected milestone is implemented.
- **OPTIONAL:** defense-in-depth or a policy choice whose omission is
  acceptable if the residual risk is recorded.

## BLOCKER findings

### B-01 — The RBAC matrix is not an enforceable authorization contract

**Evidence:** [RBAC matrix](../02-security-and-invariants.md) (lines 23–49),
[ADR-09](../03-architecture-decisions.md) (line 19).

The matrix combines different operations under single rows whose cells have
different meanings:

- `View / convert / reject Requests` gives Technician and Requester `Own
  submissions`. Read literally, this may authorize them to convert or reject
  their own requests, even though conversion is intended to be the Planner
  approval step.
- `Claim / assign / reassign Work Orders` combines technician self-claim with
  broader planner assignment authority. A single named permission or policy
  for the row could let a technician assign or reassign other users.
- `Create/edit assets, change criticality` combines ordinary edits with a
  separately audited, higher-impact mutation.
- Planner execution and completion say `Yes`, while other Planner cells say
  `Scoped to site`. The blanket prose suggests they are scoped, but the table
  is ambiguous enough for different endpoints to implement different rules.
- `Read (limited)`, `Own work history`, and `Own requests only` do not define
  fields, related objects, time boundaries, or what happens after assignment,
  conversion, reassignment, or site transfer.
- Admin-only company operations such as creating sites have no existing site
  resource, contradicting the absolute rule that every authorization decision
  is `permission + site + resource`.

**Risk:** endpoints can carry an explicit policy and still use the wrong
permission or omit the ownership/assignment predicate. This creates direct
privilege-escalation and IDOR paths despite the default-deny posture.

**Required resolution:** replace the grouped matrix rows with an atomic
command/query authorization matrix. Each operation must name:

1. its permission;
2. whether the grant is company-global or attached to a site membership;
3. the site predicate;
4. the resource predicate (`own`, `assigned`, `unassigned`, and so on);
5. allowed Work Order or Request states;
6. response-field and related-resource visibility.

Explicitly decide whether one user may hold different roles at different
sites. Avoid relying on an ordinal notion such as “Requester or higher”; map
roles to explicit permissions. Split at least request read/convert/reject/
cancel, WO self-claim/assign/reassign, asset edit/criticality change, audit
read/export, and attachment read/write/unlink into distinct operations.

### B-02 — Resource site ownership and cross-site relationship invariants are undefined

**Evidence:** [asset/location model](../01-domain-and-workflows.md) (lines
103–121), [IDOR control](../02-security-and-invariants.md) (line 56).

The statement that composite keys and foreign keys include `site_id` does not
define which entity is the authority for a resource's site. An asset's current
location is mutable, but Work Orders, requests, preventive occurrences,
downtime, attachments, part rows, and audit events are historical records.
Deriving their authorization through the asset's current location can expose
old records to a new site or hide them from the original site after an asset
transfer.

The draft also does not explicitly prohibit cross-site relationships such as:

- a Location whose parent belongs to another site;
- an Asset whose parent Asset or current Location belongs to another site;
- a Request or WO referring to an asset/location/plan from another site;
- an assignee who is not an active technician at the WO site;
- an attachment ID from one site linked to a resource in another;
- a checklist, downtime interval, or part entry addressed directly by child ID
  without authorization through its WO root.

**Risk:** a site-scoped Planner can use a valid subordinate resource ID to
cross the site boundary, or a later asset move can silently change access to
historical operational and attachment data.

**Required resolution:** document an authoritative site column and same-site
constraint for every security-relevant relationship. Historical records must
retain a site snapshot rather than derive scope from a mutable parent. Every
child lookup must join through its authorized aggregate root. Derive effective
site from the locked resource relationship, never from an untrusted route or
body field.

If cross-site asset transfer is supported, define it as a privileged audited
command, require authority over both source and destination sites, lock the
affected roots, and state which history remains visible to each site.

### B-03 — The Work Order lifecycle lacks the promised normative transition table

**Evidence:** [Work Order lifecycle](../01-domain-and-workflows.md) (lines
50–69), [RBAC lifecycle permissions](../02-security-and-invariants.md) (lines
29–33), [ADR-04](../03-architecture-decisions.md) (line 14).

ADR-04 says the state machine will be encoded as a transition table with guard
predicates, but the authoritative domain document provides only a mostly
linear diagram and prose. It leaves contradictory or undefined cases:

- `Scheduled` requires an assignee and planned date, but self-claim permits an
  *unassigned* `Scheduled` order.
- `Cancelled` is reachable from every non-closed state, which includes
  `Completed`; the intended distinction between cancelling work and voiding a
  recorded completion is unclear.
- `Closed` is called an immutable ledger record, while `Reopen` changes a
  Closed WO back to `InProgress` or `Open`.
- The legal direction for unschedule, reschedule, unassign, and reassignment is
  absent, especially while `InProgress` or `OnHold`.
- It is unclear whether an `Open` claimed WO may start immediately or must
  first transition through `Scheduled`.
- Planner may execute, complete, and close the same WO, although closure is
  described as supervisor verification.
- Reopen does not define a new execution-cycle identity or how prior completion
  evidence, timestamps, closure facts, KPIs, and floating PM dates are
  superseded without being overwritten.
- Cancellation and reopen do not define treatment of open labor/wrench/
  downtime intervals, assignments, pending attachments, and outbox effects.

**Risk:** separate API endpoints, bulk actions, Kanban drag operations, and job
handlers can accept inconsistent transitions or silently mutate terminal-state
history.

**Required resolution:** add one normative row per domain command containing
source state, target state, actor permission, resource predicate, mandatory
guards, atomic side effects, audit reason, retry result, and forbidden-state
result. Completion and closure guards must explicitly address checklist
satisfaction, safety-critical results, validated evidence, labor requirements,
open intervals, downtime classification, and verification. Model reopen as a
new execution cycle or equivalent versioned facts rather than overwriting the
prior cycle.

### B-04 — Terminal and scheduler races outside the WO root protocol are uncovered

**Evidence:** [Request conversion](../01-domain-and-workflows.md) (lines
31–41), [preventive suppression](../01-domain-and-workflows.md) (lines 80–101),
[concurrency table](../02-security-and-invariants.md) (lines 124–132).

Two independent holes remain:

1. Request/WO uniqueness prevents two WOs for the same request, but does not
   prevent `Convert` racing `Reject` or `Cancel`. Without a locked or
   conditional Request transition, a request can finish Rejected/Cancelled
   while a WO created by the racing conversion remains valid.
2. Unique `(plan_id, scheduled_for)` prevents duplicate generation of the same
   nominal occurrence. It does not enforce `SuppressIfOpen` across *different*
   scheduled dates. Nor does the table serialize generation with plan
   pause/edit, prior WO completion, floating-next-date calculation, or reopen.

`FOR UPDATE SKIP LOCKED` is work distribution, not by itself a lasting claim;
correctness depends on holding the plan lock through revalidation and the
atomic occurrence/WO commit.

**Required resolution:**

- Give every Request terminal command a Request-root protocol: lock or
  conditional `New -> terminal` update, then create/link the WO and audit in
  the same transaction. Exactly one of Convert, Reject, or Cancel may win.
- Serialize preventive generation on the plan root, revalidate enabled state
  and schedule revision under that lock, and enforce at most one active
  generated WO for a suppressing plan through a database constraint, active
  occurrence pointer, or equivalent invariant.
- Define deterministic lock ordering between plan and WO operations and the
  policy for schedule edits, floating completion, and reopening after the next
  occurrence has already been generated.

### B-05 — The attachment pipeline has a validation-to-use race and an unbound upload capability

**Evidence:** [attachment pipeline](../02-security-and-invariants.md) (lines
88–112), [ADR-11](../03-architecture-decisions.md) (line 21).

The current design appears to validate and activate the same object key that
the client receives permission to PUT. A presigned PUT is a bearer capability
until it expires and may be reusable. If the client can overwrite the key
after the worker validates it, downloaders receive bytes that were never
validated. The API's pre-signing check also validates only the client's
claimed type and size unless the storage policy and finalizer verify the actual
object.

The design does not require the upload/finalization request to remain bound to
the initiating actor, site, intended parent resource, exact object version, or
checksum. A guessed or disclosed upload ID could therefore become an
attachment-linking IDOR.

**Required resolution:** define an upload-intent record bound to actor, site,
parent resource/type, random quarantine key, maximum bytes, declared allowed
type, expiry, and state. The finalizer must reauthorize against the current
parent resource and verify actual stored length, version, and checksum.

Validate an immutable quarantine-object version. Re-encoded images or accepted
documents must be promoted/copied to a different, non-client-writable clean
key; only that immutable clean version may become `Active`. Quarantine objects
must never have a download route and need bounded expiry/orphan cleanup.
Activation, link, unlink, replacement, and deletion must use atomic state
transitions and the relevant resource-root lock.

### B-06 — The idempotency namespace is global despite being described as scoped

**Evidence:** [idempotency design](../02-security-and-invariants.md) (lines
148–166), [ADR-08](../03-architecture-decisions.md) (line 18).

Uniqueness by `(operation_name, key)` creates a global namespace. Two users or
sites can collide, maliciously pre-claim predictable keys, or receive a prior
result if a replay handler looks up and returns the record before performing
current authorization. The specified request hash does not explicitly bind
server-derived identity, effective site, target resource, or operation-schema
version.

The document also does not state that the idempotency record and domain change
commit in the same transaction. Without that atomicity, a crash can leave a
completed mutation without a replay record or a replay record without its
result.

**Required resolution:** namespace a key by operation plus authenticated
principal/client and effective site or other business scope. Define a safe
anonymous scope for public intake. Include every security-relevant
server-derived value and an operation version in the canonical request hash.
Commit the in-progress/completed idempotency state and mutation atomically,
with clear behavior for concurrent matching requests and failed operations.
On every replay, reauthorize current access to the resulting resource before
returning it; a key must never resurrect access after membership removal,
resource transfer, or permission loss.

## IMPORTANT findings

### I-01 — Membership revocation and assignee eligibility can race authorized writes

**Evidence:** [identity and current-scope requirements](../02-security-and-invariants.md)
(lines 38–49 and 59), [WO locking protocol](../02-security-and-invariants.md)
(lines 114–122).

Checking current membership inside a request is necessary but not sufficient.
If the membership row is read and then revoked before the protected write
commits, the command can succeed under stale authority. Assignment can likewise
race removal, suspension, or a role change for the target technician.

For sensitive writes, lock the relevant active membership/target-user row or
make active membership and permission an atomic predicate of the write.
Define lock ordering with resource roots. Assignment and self-claim must prove
that the target/current user is active, has the required site-level role or
permission, and belongs to the WO site at commit time.

### I-02 — Completion can race asynchronous evidence and interval changes

**Evidence:** [checklist evidence](../01-domain-and-workflows.md) (lines
128–136), [downtime rules](../01-domain-and-workflows.md) (lines 138–149),
[child-edit race](../02-security-and-invariants.md) (line 128).

A `PhotoRequired` item can be linked to an object that is still Pending and
then fail asynchronous validation after the WO has completed. An attachment
worker may not be treated as a “Work Order-scoped mutation” unless explicitly
required to lock the WO root. Open labor/wrench or downtime intervals create
similar invalid completion/closure outcomes.

Only an `Active` attachment validated for the exact checklist item should
satisfy evidence. Upload activation, link/unlink, and validation-failure
handling must participate in the WO locking protocol. Completion and closure
must reject open labor/wrench/downtime intervals and atomically freeze the
evidence/checklist version used for that execution cycle.

### I-03 — Magic-byte validation does not make manuals or image parsing safe

**Evidence:** [attachment scope and AV decision](../02-security-and-invariants.md)
(lines 90–112), [ADR-11](../03-architecture-decisions.md) (line 21).

Magic bytes establish likely format, not harmless content. A valid PDF may
contain active content or exploit a desktop reader. Raster inputs can attack
the validation worker through oversized dimensions, decompression bombs,
malformed metadata, or decoder vulnerabilities. `Content-Disposition:
attachment` reduces browser execution but does not protect the employee who
opens a malicious manual.

Before M4, choose one defensible boundary:

- restrict uploads to bounded raster images that are decoded and re-encoded;
- require AV/content disarm and reconstruction before manuals become Active;
  or
- explicitly accept and document the residual document-to-workstation risk.

In all cases, isolate the worker and enforce byte, pixel, page, recursion,
memory, CPU/time, per-user, and per-site storage limits. The statement that
magic bytes and image re-encoding already close the realistic attack surface
should be narrowed.

### I-04 — The optional public QR flow changes the locator's security role

**Evidence:** [QR strategy](../02-security-and-invariants.md) (lines 69–86),
[mobile scan flow](../04-frontend-ia.md) (lines 151–158).

For authenticated v1 use, an opaque locator plus normal authorization is a
reasonable discovery mechanism. Once anonymous reporting is enabled, a
photographed, copied, logged, or leaked permanent locator becomes a reusable
public targeting token. Rate limiting alone does not resolve targeted spam,
location disclosure, or permanent compromise of a printed code.

The v1 wording is also ambiguous: one sentence implies public tag/name/location
might be returned without full authorization, while the next says
unauthenticated users are redirected to login. State explicitly that v1
reveals no asset data before authentication and authorization.

If the public feature is implemented, use a distinct purpose-bound,
revocable/rotatable public-report locator rather than the internal asset ID or
stable internal locator. Minimize public asset/location data, moderate or
deduplicate submissions, and rate-limit by multiple dimensions including
asset, network source, and deployment-wide volume. Validate login return URLs
as local paths. The post-scan WO list must apply WO-level visibility—asset/site
access alone must not expose unassigned or other technicians' work.

### I-05 — Signed downloads and PWA caching have undefined revocation behavior

**Evidence:** [signed download design](../02-security-and-invariants.md) (lines
97–100), [PWA caching](../04-frontend-ia.md) (lines 141–149).

A signed download URL is a bearer capability until expiry. Membership removal,
reassignment, attachment unlink, or WO closure does not revoke an already
issued URL unless the object/version or signing layer changes. “Short-lived”
is not testable without a maximum TTL and an accepted revocation window.

Every URL issuance must authorize through the attachment's current parent
resource, sign immutable version identity and forced response headers, and use
a documented maximum TTL. Keep storage private. If immediate revocation is a
requirement, serve through an authorizing proxy or change the object/version
on revocation.

The PWA service worker should not cache authenticated API responses,
attachments, audit data, or signed URLs by default. Define cache purging on
logout/user change and safe behavior on shared technician devices.

### I-06 — “JWT or cookie” defers material browser authentication controls

**Evidence:** [authentication decision](../02-security-and-invariants.md)
(lines 44–49).

Cookie authentication requires CSRF protection and restrictive
`Secure`/`HttpOnly`/`SameSite` behavior. A browser-held bearer JWT raises token
storage, XSS exfiltration, refresh rotation, and revocation questions. The two
patterns cannot safely remain interchangeable during implementation.

M0 should select the browser session pattern and specify OIDC Authorization
Code + PKCE, state and nonce validation, local-only return URLs, secure session
rotation, logout, membership-change invalidation, and anti-CSRF controls for
every state-changing cookie-authenticated endpoint. Avoid persistent browser
storage for bearer tokens.

### I-07 — The threat model is too HTTP-endpoint-centric

**Evidence:** [threat-model controls](../02-security-and-invariants.md) (lines
51–67), [worker/outbox architecture](../03-architecture-decisions.md) (lines
31–43), [SignalR decision](../03-architecture-decisions.md) (line 24).

An automated assertion that every endpoint has *a* policy cannot show that it
has the correct permission, site constraint, ownership rule, state guard, or
response-field filter. Several sensitive surfaces are not ordinary controller
commands:

- list, count, search, dashboard, calendar, export, and audit/history
  projections;
- direct child-resource endpoints and attachment downloads;
- Quartz jobs, outbox handlers, projections, and service identities;
- SignalR negotiation, site-group subscription, reconnection, broadcast, and
  membership revocation;
- batch assign/reschedule and Kanban transition endpoints;
- cached frontend/PWA data.

Add negative authorization cases for every role × site relation × resource
relation × relevant state, including empty/list/cardinality responses. Define
least-privilege service identities. Outbox consumers and projections must
deduplicate on stable event/message identity because delivery is at least once.
SignalR group membership and every broadcast must be server-derived and
site-filtered, with revocation/reconnect behavior specified.

## OPTIONAL findings

### O-01 — Narrow the UUIDv7 security claim

**Evidence:** [QR locator claim](../02-security-and-invariants.md) (lines
69–76).

“This alone defeats enumeration regardless of what happens after the scan” is
too absolute. A correctly generated UUIDv7 makes blind online guessing
impractical, but does not prevent disclosure through labels, photos, logs,
referrers, analytics, screenshots, or insecure downstream authorization.
UUIDv7 also exposes approximate generation time by design.

Keep UUIDv7 if sortability is useful, require a cryptographically secure
implementation and sufficient random bits, avoid putting the locator in
third-party telemetry/referrers, and describe it as anti-guessing rather than
authorization. A separate rotatable public alias is preferable for anonymous
QR features.

### O-02 — Prevent all overlapping downtime intervals, not only two open intervals

**Evidence:** [downtime invariant](../01-domain-and-workflows.md) (lines
138–149).

A partial unique index prevents two intervals with `ended_at IS NULL`; it does
not prevent overlapping closed intervals, nor an interval closing across an
existing range. Because intervals may be created through different Work Order
roots for the same asset, WO locking alone does not serialize this invariant.

If KPI correctness requires non-overlap, use an asset-level lock and/or a
PostgreSQL exclusion constraint over an asset/time range, with explicit
boundary semantics. If overlaps are legitimate for partial derating, document
how they contribute to downtime and availability rather than silently double
counting.

### O-03 — Append-only DB permissions do not prevent forged audit events

**Evidence:** [audit design](../02-security-and-invariants.md) (lines 168–185).

Removing `UPDATE` and `DELETE` protects existing events from the ordinary
runtime role, but unrestricted `INSERT` still permits fabricated audit rows if
the application or DB credential is compromised. Database owners and migration
roles can also alter history. The current design is append-only, not
tamper-evident or non-repudiable.

If stronger evidentiary integrity is required, constrain inserts through a
database function/trigger, separate migration and runtime credentials, export
events to an independently controlled sink, and optionally use hash chaining.
Otherwise, document the trust boundary and avoid claiming protection from all
audit tampering.

### O-04 — Decide whether closure requires separation of duty

**Evidence:** [lifecycle definition](../01-domain-and-workflows.md) (lines
52–61), [role permissions](../02-security-and-invariants.md) (lines 31–33).

Planner can execute, complete, and close a WO, so the same actor may produce
and verify the completion evidence. This is not inherently insecure for every
CMMS, but it is weaker than the phrase “supervisor verified” implies.

Choose and document one rule: allow self-verification and describe closure as
planner confirmation, prohibit `closed_by == completed_by`, or require a
distinct permission only for selected criticality/safety classes. Include the
choice in transition and authorization tests.

## Controls that are sound as drafted

The following are useful foundations and should be retained:

- server-derived site/actor/state fields and allow-listed command DTOs;
- default-deny intent with application-boundary authorization;
- atomic conditional self-claim for competing technicians;
- WO-root locking for lifecycle and child mutations;
- same-transaction audit and outbox writes;
- natural occurrence uniqueness for duplicate execution of the same
  preventive schedule slot;
- private random object keys, SVG rejection, raster re-encoding, EXIF removal,
  and forced-download headers;
- identical forbidden/not-found behavior, bounded queries/exports, and
  security event logging.

These controls do not eliminate the findings above; they provide the right
primitives for resolving them.
