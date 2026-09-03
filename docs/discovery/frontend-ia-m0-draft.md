# CMMS — Frontend Information Architecture & UX Proposal (M0 Draft)

**Milestone:** M0 (Discovery & Architecture)
**Status:** Draft proposal — documents only, no app code/scaffold at this stage.
**Scope:** Frontend IA and UX direction for the Work Order / Asset lifecycle product:
Asset → Request → Work Order → Planning/Assignment → Execution/Checklist → Completion → History → Reporting/KPIs, plus a Preventive Maintenance (PM) calendar flow.

This document runs in parallel with a separate domain/UX research track. Open questions for that track are listed at the end.

---

## 1. Navigation Structure (Desktop Admin Shell)

Persistent left rail + top bar, not a hamburger-heavy SPA feel — this is ops software people live in for hours, so nav should stay put and be scannable, not chase trends.

**Left rail (icon + label, collapsible to icon-only):**
- Dashboard
- Assets
- Requests (maintenance requests inbox)
- Work Orders (board + list toggle)
- Planning / Calendar (PM schedule + technician assignment)
- Reports / KPIs
- Settings (admin: users, locations, asset categories, checklist templates)

**Top bar:** global search (asset tag / WO number / requester), org/site switcher (multi-site is common in industrial CMMS — confirm with domain research), notifications, theme toggle, user menu.

**Secondary nav pattern:** each top-level section (e.g., Assets, Work Orders) gets its own local sub-nav or filter rail on the left of the content area — don't overload the global rail with third-level items.

---

## 2. Key Screens

### Dashboard
Role-aware (admin/planner view vs. technician view differs sharply — see Section 5). Desktop: KPI tiles (open WOs, overdue PM, MTTR, asset downtime), a work-order-status breakdown, and an "attention needed" list (overdue, unassigned, high-priority). Avoid a wall of chart widgets — 4–6 high-signal tiles plus one actionable list beats a dashboard-template grid.

### Asset List
Dense, filterable table (category, location, status, criticality). This is the one screen where a data-grid density mode matters — industrial users often manage hundreds/thousands of assets. Saved filters/views are worth planning for.

### Asset Detail
Header (tag, name, location, status, criticality badge) + tabs: Overview (specs, hierarchy/parent-child if applicable), Work Order History, Maintenance Schedule (linked PM plans), Documents/Manuals, QR/asset tag info. The QR code here is the bridge to the mobile technician flow.

### Maintenance Request
Lightweight intake form (can be filled by a non-technician/requester role): asset (or location if asset unknown), issue description, priority, photo attach. Request list view with a clear "convert to Work Order" action — Request and Work Order should be visually distinct states, not the same object relabeled.

### Work Order Board / Detail
**Board:** Kanban by status (New → Assigned → In Progress → On Hold → Completed → Closed) — this is the highest-traffic screen for planners, so column WIP visibility and drag-assign matter. List view as an alternate for bulk ops/filtering.

**Detail:** asset context, assigned technician(s), priority/SLA, linked checklist, parts/labor if in scope, activity/audit log, and completion evidence (photos, notes, signature) once closed.

### Maintenance Calendar (Preventive Maintenance)
Month/week/agenda views of scheduled PM tasks by asset/technician. This is where recurrence rules (frequency, meter-based triggers if applicable) surface — flag meter-based PM (runtime hours, cycles) as an open question for domain research since it changes the calendar's data model significantly.

### Technician Mobile Flow
Treated as a distinct product surface, not a responsive shrink of desktop. See Section 5.

### Reports / KPIs
Standard set to anchor: MTTR, MTBF, PM compliance %, WO aging, asset downtime, cost by asset/category. Table-first with export, chart-second — reports in B2B ops tools get exported to Excel far more than they get admired as visualizations.

---

## 3. Visual Direction

### Positive direction (not just an avoid-list)
- Dense, information-forward layouts — whitespace used for grouping/hierarchy, not padding for its own sake
- Restrained radius (small, consistent — think 4–6px, not pill-shaped everything)
- Flat surfaces with hairline borders for separation, not shadow/glow layering
- Status communicated through color-coded badges/labels + icon, never color alone (accessibility, and colorblind technicians in the field)
- Motion only for state feedback (drag-drop, save confirmation) — no decorative transitions
- Typography: one workhorse sans (system-ui or a neutral like Inter), tight-ish line height for tables, generous for reading panes
- A muted, functional palette: neutral grays as the base, one brand accent, and a semantic status set (success/warn/danger/info) that's desaturated enough not to compete with status badges everywhere

### Explicit avoid-list
Purple/blue SaaS gradients, glassmorphism/blur panels, glow/neon accents, oversized border-radius, particle/hero animations, generic "AI dashboard" stat-card-with-sparkline clichés used decoratively rather than functionally.

### Reference category, not literal clone
Think Linear's density discipline + industrial/SCADA-adjacent seriousness (e.g., the visual restraint of tools like Fiix, UpKeep, or general enterprise ops software) rather than consumer SaaS.

---

## 4. Theming: Light / Dark / System (No Flash)

**Token architecture:** semantic tokens only in components (`--surface`, `--surface-raised`, `--text-primary`, `--text-muted`, `--border`, `--accent`, `--status-success/-warn/-danger/-info`), never raw color values in component code. Two token sets (light/dark) resolve to the same semantic names, so no component-level branching.

**No flash-of-wrong-theme:** resolve theme before first paint — read persisted preference (or `prefers-color-scheme` for "System") in a blocking inline script in `<head>`, set a `data-theme` attribute (or class) on `<html>` synchronously, before CSS/framework hydration runs. This is a solved pattern but must be planned at the architecture level now (it dictates where theme state lives — can't be purely a React-context-after-mount solution).

**"System" mode:** listen for `prefers-color-scheme` media query changes live (user flips OS theme while app is open) rather than only reading it once at load.

---

## 5. Mobile/Tablet Strategy: Technician Flow vs. Desktop Admin

Treat these as **two products sharing a design system**, not one responsive app.

### Desktop-first (admin/planner)
Dashboard, asset list, work order board, calendar, reports. Data-density and multi-pane layouts assume a mouse and a wide viewport. Tablet here degrades gracefully (board becomes scrollable columns, table gets horizontal scroll) but isn't the primary target.

### Mobile-first (technician)
A narrow, linear, single-task-at-a-time flow optimized for gloves-on/one-handed/outdoor-lighting use:

1. **QR scan** → lands directly on Asset (skip navigation entirely)
2. **Asset** → shows open/assigned Work Order(s) for that asset, big tap targets
3. **Work Order** → Start button, large and unambiguous
4. **Checklist** → step-by-step, one item's focus at a time or a simple scrollable list with big checkboxes; support offline-capable input (field connectivity is often poor — flag as a technical requirement for domain research to validate)
5. **Notes/Evidence** → camera capture, voice-to-text if feasible, minimal typing
6. **Complete** → explicit confirmation step, maybe signature capture

**Design implications:** high contrast (outdoor sun glare), large touch targets (44px+ minimum, gloves), minimal reliance on hover/secondary actions, offline-first data handling as a real architectural requirement (not just a nice-to-have), and a visually distinct "field mode" chrome (simpler top bar, no admin nav) so technicians never see the planner's complexity.

**Delivery vehicle** is an open question worth flagging to the domain/UX research track: responsive PWA (installable, camera/offline via service worker) vs. native wrapper. PWA is the leaner default assumption for M0 IA purposes unless domain research surfaces a hard requirement (e.g., barcode scanner hardware integration) that pushes toward native.

---

## 6. Open Questions for Domain/UX Research Track

- Multi-site/multi-tenant scope — does one org manage multiple physical sites/plants?
- Asset hierarchy depth (parent/child/component assets)?
- Meter-based PM triggers (runtime hours/cycles) vs. calendar-only recurrence?
- Parts/inventory management in scope for M0/M1, or a later milestone?
- Offline requirement severity for technician flow — how unreliable is field connectivity at target customer sites?
- Approval workflows (does a Request need sign-off before becoming a Work Order)?

---

*End of M0 frontend IA draft. Proposal only — no implementation started.*
