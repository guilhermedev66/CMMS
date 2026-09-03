# Frontend Information Architecture & Visual Direction

Synthesized from `docs/discovery/frontend-ia-m0-draft.md` (Claude Frontend's
IA proposal) and `docs/discovery/antigravity-m0-research.md` (Section 2 —
UX research and design tokens), reconciled with the domain/scope decisions
in `docs/01-domain-and-workflows.md`.

## Two products sharing one design system

Desktop admin/planner console and the mobile technician flow are treated as
two surfaces, not one responsive shrink of the other — confirmed by both
research tracks independently. Desktop assumes a mouse, a wide viewport, and
someone managing dozens of Work Orders per hour. Mobile assumes gloves,
outdoor glare, one-handed use, and a single task at a time.

## Desktop navigation

Persistent left rail (collapsible to icon-only) + top bar — not a
hamburger-heavy SPA. This is software people live in for hours.

- **Left rail:** Dashboard, Assets, Requests, Work Orders, Planning/Calendar,
  Reports/KPIs, Settings (users, sites, asset categories, checklist
  templates — Admin only).
- **Top bar:** global search (asset tag / WO number), site switcher (a
  company can have multiple physical sites — this is *location* switching,
  not a SaaS tenant switcher), notifications, theme toggle, user menu.
- Each top-level section gets its own local filter/sub-nav rail rather than
  overloading the global rail with third-level items.
- `Cmd+K`/`Ctrl+K` command palette (fuzzy search across assets, Work Orders,
  technicians) — genuinely useful for a dense ops tool, but a stretch item,
  not required for any milestone's DoD.

## Key screens

**Dashboard** — role-aware. Planner/Admin view: a KPI ribbon (Availability,
MTBF, MTTR, open Work Orders by type, backlog in crew-weeks — see
`01-domain-and-workflows.md` for the formulas) plus one "attention needed"
list (overdue, unassigned, high-priority), not a wall of decorative chart
widgets. Technician view: "assigned to me today," nothing else.

**Asset list** — dense, filterable table (category, location, status,
criticality). Assumes hundreds of rows; a density toggle and saved
filters/views matter more here than on almost any other screen.

**Asset detail** — header (tag, name, location, status, criticality badge)
+ tabs: Overview, Work Order History, Maintenance Schedule (linked plans),
Documents, QR info. This is the bridge into the mobile technician flow.

**Maintenance Request** — lightweight intake form (asset or location if
asset unknown, description, priority, optional photo). List view with an
explicit "Convert to Work Order" action for Planners — Request and Work
Order are visually distinct, never the same object relabeled.

**Work Order — list / board / detail.** Two view modes toggled without
losing filter state:
- **Grid** (default for planners): sticky header, frozen key columns (WO
  number, priority badge, asset tag), inline status control, batch actions
  (bulk assign, bulk reschedule).
- **Kanban**: columns matching the lifecycle (`Open → Scheduled →
  InProgress → OnHold → Completed → Closed`), cards show priority,
  asset, assignee, checklist progress, hold-reason badge when stalled.
  Drag-and-drop triggers the same guarded transitions as the API — a drag
  that violates a transition rule is rejected client-side and server-side.
- **Detail**: asset context, assignee, priority, linked checklist, parts/
  labor if applicable, activity/audit log, completion evidence.

**Maintenance Calendar** — month/week/agenda views of scheduled preventive
Work Orders. Since v1 preventive scheduling is calendar-only (no
meter-based), the calendar's data model stays simple — no per-asset
runtime-hours widget needed yet.

**Reports/KPIs** — table-first with export, chart-second. B2B ops tools get
their reports exported to spreadsheets far more than admired as
visualizations; don't over-invest in chart polish over correct, exportable
numbers.

**Technician mobile flow** — see below.

## Visual direction

**Positive direction:**
- Dense, information-forward layouts; whitespace used for grouping, not
  padding for its own sake.
- Restrained radius — 4–6px max, never pill-shaped.
- Flat surfaces, hairline 1px borders for separation — no shadow/glow
  layering for elevation.
- Status communicated via a color-coded badge **+ icon**, never color alone
  (accessibility; also genuinely useful for colorblind technicians in the
  field).
- Motion only for state feedback (drag confirm, save toast) — no decorative
  transitions.
- Typography: one workhorse sans (Inter or system-ui) for UI chrome; **all**
  data — asset tags, WO numbers, serials, meter readings, costs,
  timestamps — rendered in a monospaced, tabular-figure font (e.g. JetBrains
  Mono / `font-mono tabular-nums`) so columns don't jitter and decimals
  align. This is a concrete, checkable rule for Figma and implementation.

**Explicit avoid-list:** purple/blue SaaS gradients, glassmorphism/blur
panels, glow/neon accents, oversized border-radius, decorative hero
animation, generic "AI dashboard" stat-card-with-sparkline used
decoratively rather than functionally.

**Reference category, not a literal clone:** Linear's density discipline +
the visual restraint of serious industrial ops tools (Fiix, UpKeep) rather
than consumer SaaS.

### Color tokens (starting point for Figma / design system)

```
Neutral (Zinc/Slate):
  bg (light)        #FFFFFF / #F8FAFC (canvas)
  bg (dark)         #09090B / #18181B (surface)
  border (light)    #E2E8F0 / #CBD5E1
  border (dark)     #27272A / #3F3F46
  text primary      #0F172A (light) / #F8FAFC (dark)
  text secondary    #64748B (light) / #A1A1AA (dark)

Semantic status (functional only, desaturated enough not to fight badges):
  P1 / Emergency / Down     #DC2626 (light) / #EF4444 (dark)
  P2 / Warning / On Hold    #D97706 (light) / #F59E0B (dark)
  Operational / Running     #16A34A (light) / #22C55E (dark)
  Preventive / Scheduled    #2563EB (light) / #3B82F6 (dark)
  Draft / Neutral / Off     #4B5563 (light) / #71717A (dark)
```

These become semantic tokens in code (`--surface`, `--text-primary`,
`--status-danger`, etc.) — components never reference raw hex values.

## Light / Dark / System (no flash)

Theme resolves **before first paint**: a small blocking inline script in
`<head>` reads the persisted preference (or `prefers-color-scheme` for
"System") and sets `data-theme` on `<html>` synchronously, before
React/CSS hydration — this can't be a "set it in a context after mount"
implementation, it has to be decided at this architectural level now.
"System" mode also listens live for `prefers-color-scheme` changes (user
flips OS theme mid-session), not just a one-time read at load. Every token
above has both a light and dark value from the start — this is foundational
per the brief, not a final-week retrofit.

## Mobile / technician flow

Delivery vehicle: a responsive **PWA** (installable, camera access, basic
service-worker caching) is the default assumption — leaner than a native
wrapper, and nothing in scope currently forces native (no barcode-scanner
hardware integration requirement). Full offline-first data sync is
explicitly **out of scope** for v1 (per `docs/00-product-vision.md`) — the
PWA should degrade gracefully on poor connectivity (retry, don't lose form
input) rather than promise guaranteed offline operation.

Flow: **QR scan** (camera FAB, decodes the opaque asset locator — see
`02-security-and-invariants.md`) → lands directly on **Asset** (skip
navigation) → shows open/assigned Work Order(s) for that asset with big tap
targets → **Work Order** detail with an unambiguous **Start** action →
**Checklist** runner (large toggles/inputs, one item's focus at a time on
small screens; numeric items bring up a numeric keypad with immediate
green/red tolerance feedback) → **Notes/Evidence** (camera capture, minimal
typing) → **Complete** (explicit confirmation).

Design implications: high contrast for outdoor glare, ≥44px touch targets
for gloved hands, minimal reliance on hover/secondary actions, a visually
distinct "field mode" chrome (simplified top bar, no admin nav — a
technician never needs to see planner-level complexity).

## Resolved open questions

(Carried over from the frontend draft's open-questions list — resolved here
so M1 frontend work isn't blocked on them.)

- **Multi-site:** yes, a company can have multiple physical sites — but this
  is location scoping, not multi-tenant SaaS (see `01-domain-and-workflows.md`).
- **Asset hierarchy depth:** Location tree + Asset with optional parent
  Asset for sub-components — not the full ISA-95 7-level model.
- **Meter-based PM:** out of scope for v1; calendar-only.
- **Parts/inventory:** in scope from M4, lean/record-only — no
  stock/warehouse UI needed.
- **Offline severity:** not full offline-first; PWA with graceful
  degradation.
- **Approval workflow:** Planner/Supervisor conversion of a Request *is*
  the approval — no separate approval screen/state.
