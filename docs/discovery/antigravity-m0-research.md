# CMMS — Maintenance & Asset Management System
## M0 Discovery & Architecture Research Document
**Document Title:** Antigravity CMMS M0 Research  
**Document Type:** Discovery, Domain Modeling & Architectural Decision Record (ADR)  
**Standard Structure:** EVIDENCE $\rightarrow$ FINDING $\rightarrow$ RECOMMENDATION $\rightarrow$ DECISION  
**Target Domain:** Industrial Enterprise Asset Management (EAM) / Computerized Maintenance Management System (CMMS)  
**Workspace Directory:** `/mnt/c/dev/Maintenance & Asset Management System (CMMS)`  

---

## Executive Overview & System Boundary

This document formalizes the operational, ergonomic, and engineering foundations for a modern, industrial-grade **Computerized Maintenance Management System (CMMS)** and **Enterprise Asset Management (EAM)** platform. 

The system governs two converging industrial operational lifecycles:
1. **The Corrective Maintenance Flow:**
   $$\text{Asset/Location} \longrightarrow \text{Failure Report/Request} \longrightarrow \text{Triage/Work Order} \longrightarrow \text{Planning/Assignment} \longrightarrow \text{Execution} \longrightarrow \text{Checklist/Parts/Costs/Downtime} \longrightarrow \text{Completion/Verification} \longrightarrow \text{Asset History} \longrightarrow \text{KPI Calculations}$$
2. **The Preventive Maintenance (PM) Flow:**
   $$\text{Maintenance Plan} \longrightarrow \text{Recurrence Engine} \longrightarrow \text{Distributed Background Scheduler} \longrightarrow \text{Preventive Work Order} \longrightarrow \text{Execution} \longrightarrow \text{Next Schedule Calculation}$$

---

# SECTION 1: DOMAIN ARCHITECTURE & CMMS WORKFLOWS

---

### 1.1 Reactive & Corrective Maintenance Workflow

#### EVIDENCE
Industrial field operations across discrete manufacturing, chemical processing, logistics facilities, and utilities adhere to standardized failure mitigation procedures (governed by SMRP Best Practices, ISO 14224, and OSHA 1910.147). In real-world plant environments, maintenance failures present distinct operational phases:
1. **Identification & Request Intake:** An equipment operator, shift supervisor, or sensor notices an anomaly (e.g. abnormal vibration, fluid leak, total motor seizure) and submits an unvetted maintenance request.
2. **Triage & Authorization:** Plant planners or maintenance supervisors review incoming requests to eliminate duplicates, verify safety hazards, assign priority (P1 Emergency Breakdown to P4 Cosmetic/Low), and elevate approved requests into formal Work Orders (WOs).
3. **Planning & Resource Staging:** Planners assign required craft trades (e.g., Electrician, Millwright, Lubrication Tech), allocate spare parts from the storeroom (MRO inventory reservation), and estimate labor hours.
4. **Dispatch & Execution:** The supervisor assigns the WO to an individual technician or crew. The technician clocks in, executes Lockout/Tagout (LOTO), conducts diagnostic troubleshooting, replaces defective components, and completes inspection checklists.
5. **Close-out, Cost Accumulation & RCA:** The technician records actual wrench time, logs consumed parts and quantities, records exact equipment downtime start/end, and records failure cause codes. A supervisor inspects and approves the work, closing the WO and permanently writing records to the Asset Maintenance Ledger.

#### FINDING
- Permitting arbitrary operators to directly create unmoderated Work Orders causes severe backlog contamination, duplicate tickets for the same failure, and skewed backlog metrics.
- Separating the **Maintenance Request** (intake entity) from the **Work Order** (operational execution entity) is mandatory.
- Labor hours must be captured as two distinct quantities: **Total Downtime** (clock duration the machine was unavailable for production) versus **Active Wrench Time** (technician hands-on repair duration). Conflating these two destroys Mean Time To Repair (MTTR) and Availability calculations.

#### RECOMMENDATION
- Enforce a strict two-stage intake: `MaintenanceRequest` $\rightarrow$ `WorkOrder`. Anyone on the plant floor can submit a request (even anonymously via a localized QR portal), but only authorized Maintenance Planners, Supervisors, or Admins can promote a request into an actionable Work Order.
- Capture granular labor tickets with technician identity, hourly billing rate, craft code, start time, and stop time.
- Mandatory downtime recording: If a Work Order was initiated as a machine-down event, the system must forbid closure until exact `DowntimeStartUtc`, `DowntimeEndUtc`, and `FailureCauseCode` are logged.

#### DECISION
Adopt the canonical Corrective Maintenance Pipeline:
`Request (Intake)` $\rightarrow$ `Triage/Approval` $\rightarrow$ `Work Order (Draft/Backlog)` $\rightarrow$ `Planning (BOM/Labor/Priority)` $\rightarrow$ `Scheduled & Dispatched` $\rightarrow$ `In Progress (Wrench Time + Checklists)` $\rightarrow$ `Work Complete (Pending Verification)` $\rightarrow$ `Supervisor Sign-off / Closed` $\rightarrow$ `Asset Ledger & KPI Recalculation`.

---

### 1.2 Work Order Lifecycle States & Transition Matrix

#### EVIDENCE
Analysis of enterprise EAM platforms (IBM Maximo, SAP PM, IFS Ultimo) versus modern agile CMMS (MaintainX, Fiix, Limble) shows that simplistic 3-state models (`Open`, `In Progress`, `Closed`) fail in industrial environments because they cannot capture operational roadblocks (such as awaiting parts, waiting on production shutdown, or awaiting engineering sign-off). Conversely, 20-state workflows create cognitive fatigue and lead technicians to bypass the software entirely.

#### FINDING
An optimal industrial state machine requires exactly 8 core operational states with deterministic transition guards and role-based permissions:
1. `DRAFT`: WO is being compiled by planner (not yet visible on technician dispatch boards).
2. `SUBMITTED`: Request submitted, pending triage.
3. `READY_FOR_PLANNING` (Backlog): Approved maintenance need; awaiting parts reservation, craft allocation, and shutdown scheduling.
4. `SCHEDULED_ASSIGNED`: Scheduled with planned start date and assigned to a technician or crew.
5. `IN_PROGRESS`: Technician has commenced active work.
6. `ON_HOLD`: Work suspended due to external blocker. Must require an explicit sub-status: `AWAITING_PARTS`, `AWAITING_PRODUCTION_SHUTDOWN`, `AWAITING_EXTERNAL_CONTRACTOR`, `SAFETY_HOLD`.
7. `WORK_COMPLETED`: Technician finished hands-on tasks, completed checklist, and logged parts/labor. Awaiting supervisor audit.
8. `CLOSED`: Supervisor verified repair quality, confirmed downtime logs, and sealed the financial ledger. Immutable.
*(Additionally: `CANCELLED` as a terminal exit state with mandatory cancellation reason).*

#### RECOMMENDATION
Implement a deterministic State Machine with strict transition guards:
- Transition `DRAFT / SUBMITTED` $\rightarrow$ `SCHEDULED`: Allowed by Planner, Supervisor, Admin.
- Transition `SCHEDULED` $\rightarrow$ `IN_PROGRESS`: Automatically triggered when technician clocks in or clicks "Start Work".
- Transition `IN_PROGRESS` $\rightarrow$ `ON_HOLD`: Requires selecting a hold reason code. If `AWAITING_PARTS`, link to Purchase Order or Stock Reservation ID.
- Transition `IN_PROGRESS` $\rightarrow$ `WORK_COMPLETED`: Guard fails if any mandatory checklist item is uncompleted, or if zero labor hours are logged.
- Transition `WORK_COMPLETED` $\rightarrow$ `CLOSED`: Allowed only by Supervisor/Admin. Calculates final labor costs, material costs, total downtime, and emits a domain event for KPI rollups.
- Immutability Guard: Once a Work Order is `CLOSED`, its labor, parts, downtime, and checklist records become strictly read-only. Corrections require an auditable administrative amendment note.

#### DECISION
Encode the Work Order State Machine in the domain layer using an explicit transition table and guard predicates. Rejections and cancellations require a mandatory textual reason code and user audit stamp.

```
       [Operator/Sensor]
               │
               ▼
         [ SUBMITTED ] ──────── (Rejected) ────────► [ CANCELLED ]
               │                                            ▲
      (Approved / Triaged)                                  │
               ▼                                            │
   [ READY_FOR_PLANNING ]                                   │
               │                                            │
           (Planned)                                        │
               ▼                                            │
    [ SCHEDULED_ASSIGNED ] ─── (Cancelled by Planner) ──────┤
               │                                            │
         (Start Work)                                       │
               ▼                                            │
    ┌──► [ IN_PROGRESS ] ───── (Cancelled by Supervisor) ───┘
    │          │    ▲
(Resume Work)  │    │ (Put On Hold)
    │          ▼    │
    └─── [  ON_HOLD  ] (Sub-reasons: Parts, Production, Contractor)
               │
          (All Tasks Done & Labor Logged)
               ▼
      [ WORK_COMPLETED ]
               │
       (Supervisor Sign-off)
               ▼
          [  CLOSED  ] (Immutable Ledger Record)
```

---

### 1.3 Preventive Maintenance (PM) Scheduling Patterns

#### EVIDENCE
Equipment failure distributions (Nowlan & Heap reliability research) prove that 89% of industrial component failures are not age-related, but 11% exhibit clear wear-out patterns that benefit directly from scheduled preventive intervention. Modern CMMS systems support three primary PM scheduling strategies:
1. **Calendar Time-based:** Fixed interval (e.g. every Monday, every 30 days, quarterly, annually).
2. **Meter / Runtime-based:** Usage-based (e.g. every 500 operating hours, every 10,000 cycles, every 5,000 kilometers).
3. **Condition-based (CBM):** Triggered by threshold exceedances on continuous telemetry (vibration RMS > 4.5 mm/s, temperature > 80°C).

Within Calendar PMs, industrial operations distinguish between:
- **Fixed Scheduling:** The next due date is anchored strictly to the calendar schedule, regardless of when the previous Work Order was completed. Example: Monthly filter change scheduled for the 1st of every month. If the January PM is finished on Jan 20th, the February PM is still due on Feb 1st.
- **Floating Scheduling (Completion-based):** The next due date is calculated relative to the **actual completion date** of the preceding Work Order. Example: Greasing bearings every 30 days. If the tech completes it on Jan 20th, the next PM becomes due on Feb 19th.

#### FINDING
- Fixed scheduling without suppression rules creates **"PM Stacking"**: if a technician falls behind, 3 identical overdue PM work orders accumulate for the same pump, overwhelming the technician with duplicate paperwork.
- Meter-based scheduling requires handling **meter rollovers** (e.g., an analog 5-digit mechanical hour meter flipping from 99,999 to 00,000) and **meter replacements** (replacing a broken gauge with a new gauge starting at zero).
- Industrial planners require a **Lead Time Offset (Generation Offset)**: A Work Order due on June 15th must be generated on June 8th (7 days prior) so parts can be pulled and staged before the equipment is taken offline.

#### RECOMMENDATION
1. Support both **Fixed** and **Floating** calendar schedules via a first-class strategy flag on the `MaintenancePlan`.
2. Implement **PM Suppression & Stacking Policies**:
   - Option A: *Stacking Allowed* (generates new PM even if previous is open).
   - Option B: *Suppress While Open* (if a PM Work Order generated by this plan is still in `OPEN / IN_PROGRESS / ON_HOLD`, do not generate a duplicate; flag existing WO as overdue and alert the supervisor).
3. Implement **Lead-Time Generation Offset**: Configure `LeadTimeDays` on the plan. Work Order is instantiated `N` days ahead with status `SCHEDULED` and target execution date set to the true due date.
4. Meter Tracking Engine: Support high-water mark tracking, rollover thresholds, and delta validation (flagging suspicious meter drops for supervisor review rather than silently corrupting maintenance intervals).

#### DECISION
Implement a unified `MaintenancePlan` schema supporting:
- Recurrence Type: `CalendarFixed`, `CalendarFloating`, `MeterRuntime`, `ConditionThreshold`.
- Recurrence Interval (e.g., Every `N` Days / Weeks / Months or Every `N` Runtime Units).
- `GenerationLeadTimeDays` (default: 3 days).
- `SuppressionPolicy`: `SuppressIfOpen` (default) vs `AllowStacking`.
- Dynamic Next Due Date calculator that recalculates immediately upon preceding Work Order `CLOSED` event for floating plans, or upon cron background sweep for fixed plans.

---

### 1.4 Mathematically Defensible KPI Formulas

#### EVIDENCE
Industrial reliability engineering (standards: SMRP Best Practice Guide, ISO 14224, EN 13306, IEEE 762) defines precise mathematical rules for equipment reliability and maintenance performance. Naive software formulas that divide total calendar hours by failure count produce mathematically invalid MTBF figures.

#### FINDING & RIGOROUS MATHEMATICAL FORMULATION

#### 1. MTBF (Mean Time Between Failures)
- **Common Error:** Dividing total calendar time (e.g., 720 hours in a month) by the number of breakdowns. This falsely includes downtime and planned shutdown hours as uptime.
- **Defensible Formula:** MTBF is the mathematical expectation of the **operating time between unscheduled operational failures**:
  $$\text{MTBF} = \frac{\text{Total Operating Time}}{\text{Number of Unscheduled Failures}} = \frac{\text{Total Available Time} - \text{Total Downtime (Planned + Unplanned)}}{\text{Count of Functional Breakdown Work Orders}}$$
  For an asset operating under continuous run schedules:
  $$\text{MTBF} = \frac{\sum_{i=1}^{k} \left( t_{\text{failure, } i} - t_{\text{restart, } i-1} \right)}{k}$$
  *Boundary Conditions:* If $k = 0$ (zero failures in evaluation period), MTBF is undefined or reported as $\ge \text{Total Operating Hours}$ with a "Zero Failure" operational badge.

#### 2. MTTR (Mean Time To Repair)
- **Common Error:** Conflating Total Downtime (clock time from breakdown to operator handover) with active labor time (wrench time).
- **Defensible Formula:** Standard reliability engineering defines MTTR as the average active corrective maintenance time required to restore a failed item to operational status:
  $$\text{MTTR} = \frac{\sum_{i=1}^{k} \left( t_{\text{repair\_complete, } i} - t_{\text{repair\_start, } i} \right)}{k}$$
  In addition, the system shall compute **Mean Downtime (MDT)** to measure total production interruption (including logistics delay and parts wait):
  $$\text{MDT} = \frac{\sum_{i=1}^{k} \left( t_{\text{production\_handover, } i} - t_{\text{breakdown\_stop, } i} \right)}{k}$$

#### 3. Equipment Availability ($A$)
- **Operational Availability ($A_o$):** Reflects real-world shop floor readiness including administrative and logistics delays:
  $$A_o = \frac{\text{Actual Operating Time}}{\text{Planned Production Time}} = \frac{\text{Planned Operating Time} - (\text{Unplanned Breakdown Downtime} + \text{Planned Maintenance Downtime})}{\text{Planned Operating Time}}$$
- **Inherent Availability ($A_i$):** Reflects pure equipment design reliability under ideal support conditions:
  $$A_i = \frac{\text{MTBF}}{\text{MTBF} + \text{MTTR}}$$

#### 4. Maintenance Backlog (in Crew-Weeks)
- **Common Error:** Reporting backlog merely as a raw count of open tickets (e.g., "47 open tickets"). This is meaningless because one ticket may take 15 minutes while another takes 80 hours.
- **Defensible Formula:** Backlog measures work volume normalized by net available craft labor capacity:
  $$\text{Backlog (Weeks)} = \frac{\sum_{j \in \text{Open WOs}} \text{Estimated Labor Hours}_j}{\text{Total Available Weekly Craft Hours} \times \text{Productivity Factor}}$$
  Where:
  $$\text{Total Available Weekly Craft Hours} = (\text{Total Technicians}) \times (\text{Shift Hours/Week}) - (\text{Vacation + Training + PTO Hours})$$
  $$\text{Productivity Factor (Wrench Time Ratio)} \approx 0.65 \text{ to } 0.75 \text{ (accounting for travel, shift meetings, prep)}$$
  *Standard Industry Benchmark:* Healthy backlog is **2.0 to 4.0 weeks**. Backlog $<2$ weeks indicates overstaffing or uncaptured work. Backlog $>6$ weeks indicates dangerous maintenance deferral and impending equipment failure.

#### 5. Planned Maintenance Percentage (PMP)
$$\text{PMP} = \frac{\text{Labor Hours Spent on Planned PM Work Orders}}{\text{Total Labor Hours (PM + Reactive Breakdown + Corrective)}} \times 100\%$$
*World-Class Target:* $\ge 80\%$ Planned Maintenance, $\le 20\%$ Reactive.

#### 6. Total Cost of Maintenance & Replacement Asset Value (% RAV)
$$\text{Total Cost of Maintenance (TCM)} = \sum (\text{Internal Labor Hours} \times \text{Fully Loaded Hourly Rate}) + \sum (\text{Parts Consumed} \times \text{Unit Cost}) + \text{Contractor Invoices} + \text{Direct Expenses}$$
$$\% \text{RAV} = \frac{\text{Annual Total Maintenance Cost}}{\text{Estimated Plant/Asset Replacement Value}} \times 100\%$$
*World-Class Target:* **$2.0\% \text{ to } 3.5\%$ of RAV**.

#### RECOMMENDATION
Build an internal Analytics Aggregation Pipeline that computes these metrics strictly according to these formulas, providing drill-downs by Plant, Department, Equipment Criticality, and Date Range.

#### DECISION
Store raw operational timestamps (`DowntimeStartUtc`, `DowntimeEndUtc`, `WrenchStartUtc`, `WrenchEndUtc`, `PlannedHours`, `ActualHours`, `PartUnitCost`) directly on work order transaction tables. Never store pre-computed averages in transactional tables; compute metrics on demand or via read-model materialized views.

---

### 1.5 Asset Hierarchy & Location Modeling Conventions

#### EVIDENCE
Standards **ISO 14224** (Reliability and maintenance data for equipment in petroleum, natural gas, and petrochemical industries) and **ISA-95** (Enterprise-Control System Integration) mandate a formal separation between **Functional Locations** and **Physical Equipment (Assets)**.

```
[ ISO 14224 / ISA-95 Hierarchy ]

Level 1: Enterprise           (e.g., Apex Industrial Group)
   │
Level 2: Site / Business Unit (e.g., Ohio Manufacturing Complex)
   │
Level 3: Plant / Area         (e.g., Plant 02 - Polymer Synthesis)
   │
Level 4: Process Unit/System  (e.g., Reactor Train B)
   │
Level 5: Functional Location  (e.g., P-201A Slurry Feed Position)
   │                           [Asset Installed: Goulds 3196, S/N #98421]
   │
Level 6: Sub-Assembly         (e.g., Mechanical Seal Chamber)
   │
Level 7: Maintainable Item    (e.g., Tungsten Carbide Seal Face)
```

#### FINDING
- A **Functional Location** represents a spatial or process position in the factory (e.g. `OH-P02-LINE1-PUMP-01`). It has operational context (process flow, criticality, safety requirements) and endures indefinitely.
- A **Physical Asset (Equipment)** is a serialized, physical machine (e.g. `Goulds 3196 Centrifugal Pump`, Serial Number `SN-8841029`). It is purchased, installed, operated, removed, rebuilt in the repair shop, returned to the spares warehouse, and eventually decommissioned.
- Systems that combine Location and Asset into a single table fail when an electric motor or gearbox is pulled for overhaul and replaced with a rotating spare: historical work orders either become detached or corrupt the machine's true MTBF.

#### RECOMMENDATION
1. Implement two distinct entities:
   - `FunctionalLocation`: Recursive self-referential tree structure (`ParentId` $\rightarrow$ `FunctionalLocation`). Represents the logical plant topology.
   - `Asset`: Physical serialized hardware record with manufacturer, model, serial number, procurement date, warranty, and technical specifications.
2. Link them via a temporal join table: `AssetInstallationHistory` (`AssetId`, `FunctionalLocationId`, `InstalledAtUtc`, `RemovedAtUtc`, `InstalledByUserId`, `MeterReadingAtInstall`, `MeterReadingAtRemoval`).
3. Support recursive CTE (Common Table Expression) queries to compute rollup costs: querying a Plant Area automatically aggregates all maintenance costs from every child location and installed equipment underneath it.
4. Implement **Asset Criticality Rating (ABC Classification)**:
   - **Class A (Critical):** Direct production-stopper, single point of failure, environmental/safety risk. Strict PM compliance mandatory.
   - **Class B (Essential):** Redundant units or process buffer exists; failure degrades production within 24–48 hours.
   - **Class C (Non-critical):** Run-to-failure acceptable; zero safety or production impact.

#### DECISION
Implement the dual-hierarchy model (`FunctionalLocation` tree + `Asset` entity + `AssetInstallationHistory` temporal ledger). Work Orders are tied to both the `FunctionalLocationId` (where the failure occurred) and `AssetId` (which physical machine was serviced).

---

### 1.6 Checklist & Downtime Tracking Norms

#### EVIDENCE
OSHA 1910.147 (Lockout/Tagout), ISO 9001 (Quality Management), and industrial insurance audits require verified proof of procedure execution. Unstructured free-form text boxes fail compliance audits and prevent structured failure analysis.

#### FINDING
- Modern industrial checklists require typed step execution:
  - `BOOLEAN`: Pass / Fail (with optional conditional logic: if Fail, prompt for severity or auto-generate follow-up Work Order).
  - `NUMERIC_TOLERANCE`: Reading input with `MinAllowed`, `MaxAllowed`, `WarningLow`, `WarningHigh`, and unit of measurement (e.g., bearing temp: 65°C, bounds: 40–80°C).
  - `QUALITATIVE_CHOICE`: Single-select / Multi-select from predefined options.
  - `PHOTO_REQUIRED`: Mandatory photo upload before step can be marked complete (e.g., photo of installed lockout padlock, photo of clean strainer).
  - `LOTO_VERIFICATION`: Specific safety lock-out sign-off with safety lock box number and zero energy verification.
- Downtime Tracking requires:
  - Exact UTC timestamps: `DowntimeStartedUtc`, `DowntimeEndedUtc`.
  - Operational Classification: `FullStop` (100% production halt) vs `PartialDerating` (running at reduced throughput, e.g. 50% speed).
  - Standardized Root Cause Hierarchy (Pareto Failure Code):
    - Level 1: Failure Category (`Mechanical`, `Electrical`, `Hydraulic`, `Pneumatic`, `Instrumentation`, `Operator/Operational`).
    - Level 2: Failure Mechanism (`Wear/Fatigue`, `Contamination/Lubrication`, `Thermal Overload`, `Misalignment`, `Loose Fastener`, `Software/PLC Fault`).

#### RECOMMENDATION
- Model checklists as structured JSON-schema templates (`ChecklistTemplate`) instantiating immutable `WorkOrderChecklistExecution` records upon WO creation.
- For numeric tolerance failures, visually flag the reading in amber/red and optionally trigger an automated corrective ticket.
- Require dual sign-off on Criticality Class A equipment: Technician digital signature + Shift Supervisor review.

#### DECISION
Adopt a structured `ChecklistTemplate` engine with typed step inputs (Pass/Fail, Numeric Tolerance, Photo, LOTO, Single-Select) and a first-class `DowntimeLedger` linked to every corrective Work Order.

---

# SECTION 2: UX / FRONTEND ARCHITECTURE FOR INDUSTRIAL OPERATIONS

---

### 2.1 Product Analysis & Competitive Benchmarking

#### EVIDENCE
Evaluation of incumbent industrial systems (IBM Maximo, SAP Plant Maintenance) versus modern cloud-native CMMS solutions (MaintainX, Fiix by Rockwell Automation, Limble CMMS, UpKeep) highlights key operational UX lessons:

| Platform | Strengths | Weaknesses | Architectural Lesson for Us |
| :--- | :--- | :--- | :--- |
| **IBM Maximo** | Deep enterprise capability; extensive asset tree; granular compliance & procurement controls. | Overwhelming form bloat; dense grey 1990s UI; abysmal mobile usability; high click fatigue (7 clicks to log a part). | Retain the data depth and functional location modeling, but eliminate form bloat with modern drawer/slide-over UX. |
| **MaintainX** | Fast mobile execution; real-time chat attached to work orders; seamless photo annotation; modern high-contrast aesthetic. | Light on deep hierarchical location modeling; limited complex multi-tier parts BOM tracking. | Emulate their glove-friendly mobile checklist execution, photo markup tools, and clean typography. |
| **Limble CMMS** | Intuitive drag-and-drop PM calendar; modular custom fields; clean breadcrumb navigation. | Calendar can feel sluggish with $>500$ concurrent scheduled tasks; dense multi-asset batch operations limited. | Implement virtualized calendar and kanban boards; prioritize fast keyboard shortcuts. |
| **Fiix (Rockwell)** | Robust asset hierarchy navigation; strong parts inventory association; solid reporting widgets. | Clunky navigation between asset details and open work orders; rigid layout customization. | Implement master-detail split-pane views with persistent context and zero page reloads. |

#### FINDING
Plant personnel divide into two distinct user personas with radically different ergonomic requirements:
1. **The Desktop Persona (Maintenance Planner, Plant Supervisor, Reliability Engineer):**
   - High information density; dual-monitor workflows; 100+ work orders managed per hour; needs keyboard navigation, dense data grids (32px row height), instant multi-faceted filtering, and batch operations (bulk assign, bulk reschedule).
2. **The Mobile/Shop Floor Persona (Field Technician, Millwright, Electrician):**
   - 8-to-10-inch rugged tablets or personal smartphones; dusty, greasy hands or wearing industrial gloves; high ambient glare (outdoor yards) or dimly lit utility tunnels; intermittent Wi-Fi connectivity. Needs $\ge 48\text{px}$ touch targets, camera-first QR scanning, 1-tap labor timers, and voice-to-text note capture.

#### RECOMMENDATION
Design a responsive, dual-persona interface:
- A high-density **Desktop Operations Console** featuring split-pane asset trees, dense data grids, multi-column sorting, and slide-over inspectors.
- A streamlined **Mobile Field Technician Experience** focusing exclusively on "My Assigned Work Orders Today", camera-driven asset identification, 1-tap checklist completion, and photo evidence upload.

#### DECISION
Build the frontend using a single unified React/TypeScript codebase with responsive adaptive layouts. Use dedicated mobile execution views for active work order execution while maintaining dense, tabular views for desktop planners.

---

### 2.2 Core Operational Views & Interaction Architecture

#### 1. Asset Master-Detail / Split-Pane View
- **Left Pane (20–25% width, collapsible):** Interactive recursive tree view of Functional Locations $\rightarrow$ Assets. Features search input, active work order alert badges (red for P1, amber for P2), and inline collapse/expand.
- **Center Pane (50–55% width):** Dense Data Table displaying assets matching the selected hierarchy node, with columns: `Tag #`, `Equipment Name`, `Criticality (A/B/C)`, `Status (Running/Down/Standby)`, `Open WOs`, `Last PM Date`, `Assigned Tech`.
- **Right Slide-over Drawer (25–30% width, toggled on row select):** Contextual inspection drawer displaying asset photo, QR code badge, nameplate specs (voltage, RPM, flow rate), active meter readings, quick links to O&M PDF manuals, and a primary CTA: `+ Log Request / Work Order`. Allows drilling into an asset without navigating away from the tree.

#### 2. Work Order Management Views (Triple-View Switcher)
Planners can toggle between three operational view modes without losing active filter states:
1. **Dense Grid Mode (Default):**
   - Sticky header with frozen primary columns (`WO Number`, `Priority Badge`, `Asset Tag`).
   - Density toggle: Compact (32px row height) vs Standard (40px row height).
   - Inline status dropdown for rapid supervisor triage.
   - Batch selection bar: Bulk Assign Technician, Bulk Reschedule Due Date, Export Travel Packets (Printable PDF).
2. **Operational Kanban Mode (Shift Handover View):**
   - Swimlanes mapped to operational states: `Backlog` $\rightarrow$ `Scheduled` $\rightarrow$ `In Progress` $\rightarrow$ `Waiting on Parts` $\rightarrow$ `Review/Sign-off`.
   - Cards display priority color bar, asset tag, technician avatar, checklist progress indicator (`4/7 done`), and hold reason badge if stalled.
   - Drag-and-drop triggers state transitions, executing validation guards (e.g. dragging to `Completed` pops up the labor/parts completion modal).
3. **Maintenance Calendar & Dispatch Timeline:**
   - Resource scheduling Gantt/Calendar view showing technician work capacity vs scheduled PMs.
   - Visual conflict alerts when a technician is double-booked or an asset has overlapping scheduled outages.

#### 3. Technician Mobile Flow (Field Execution)
- **Home Screen:** Clean card list: "Assigned to Me Today" ordered by priority (`P1 Emergency` anchored to top).
- **QR Scan Floating Action Button (FAB):** 1-tap opens camera scanner; decodes asset QR sticker and navigates directly to the asset's active work orders or new request form in $<1$ second.
- **Wrench Time Stopwatch:** 1-tap "Start Work" clocks the technician in with live elapsed time ticker. Technician can pause work with an explicit reason ("Lunch", "Waiting on Storeroom Parts").
- **Checklist Runner:** Full-width rows with large toggle switches. Numeric inputs automatically bring up numeric keypad with green/red feedback for tolerance bounds.
- **Camera & Photo Markup:** Built-in photo attachment flow with native freehand pen markup (red circle/arrow) to document failure points before/after repair.

#### 4. Dashboards & KPI Widget Matrix
- **Top Metric Ribbon (Hero KPIs):**
  - `Plant Availability`: Real-time percentage gauge with month-over-month delta.
  - `MTBF`: Mean operating hours between breakdowns.
  - `MTTR`: Average repair time in hours.
  - `Active Work Orders`: Breakdown vs PM count.
  - `Backlog`: Expressed in Crew-Weeks (color-coded: Green if 2–4 wks, Amber if 4–6 wks, Red if $>6$ wks).
- **Analytical Chart Widgets:**
  - `Downtime Pareto Chart`: Horizontal bar chart ranking the top 10 failure cause codes by lost production minutes.
  - `PM Compliance Trend`: 12-week rolling line chart tracking Planned Maintenance Completed on Time vs Overdue.
  - `Cost Analysis`: Stacked bar chart showing Labor Spend vs Parts/MRO Spend vs Contractor Spend per Plant Area.

#### 5. Filters & Command Palette
- **Faceted Filter Bar:** Multi-select popovers for `Plant/Area`, `Priority (P1-P4)`, `Status`, `Assigned Craft/Technician`, `Due Date Range`. Selected filters appear as removable chip pills with an instant "Clear All" action.
- **Global Command Palette (`Cmd+K` / `Ctrl+K`):** Instant spotlight search indexing Asset Tags, Serial Numbers, Work Order numbers, and Technician names with sub-50ms fuzzy matching.

---

### 2.3 Visual Direction & Design System (Anti-Pattern Rejection)

#### EVIDENCE
Industrial control rooms, factory floor tablets, and maintenance shops operate in harsh lighting conditions. Equipment operators and maintenance managers reject software that resembles consumer social media or flashy marketing websites.

#### EXPLICIT ANTI-PATTERNS (WHAT WE STRICTLY REJECT)
1. **NO Generic "AI/Purple-SaaS" Gradients:** Absolute prohibition of saturated indigo/violet/purple gradient backgrounds, neon cyan accents, or rainbow border gradients.
2. **NO Glassmorphism or Background Blurs:** No `backdrop-blur-md`, no semi-transparent white frosted glass over colorful blobs. Industrial users require crisp, opaque, high-contrast surfaces.
3. **NO Decorative Glows or Drop Shadows:** No colored drop shadows (`shadow-indigo-500/50`) or neon pulsing halos. Elevation must be expressed through crisp 1px solid borders and subtle, neutral greyscale shadow steps.
4. **NO Excessive Rounded Corners:** No pill-shaped cards or `rounded-3xl` (24–32px) container corners. Rounded corners waste valuable pixel real estate in dense operational tables. Maximum border radius is strictly 4px to 6px (`rounded-md`).
5. **NO Artificially Sparse Layouts ("Card Soup"):** Avoid giant empty cards with 48px padding displaying single data points. Maintenance professionals demand information density, structured tabular data, and high scanning speed.

#### POSITIVE DESIGN SYSTEM SPECIFICATION

```
[ COLOR PALETTE SPECIFICATION ]

Neutral Scale (Zinc/Slate):
  Background (Light):      #FFFFFF (Pure White) / #F8FAFC (Slate 50 Canvas)
  Background (Dark):       #09090B (Zinc 950 Canvas) / #18181B (Zinc 900 Surface)
  Borders (Light):         #E2E8F0 (Slate 200) / #CBD5E1 (Slate 300)
  Borders (Dark):          #27272A (Zinc 800) / #3F3F46 (Zinc 700)
  Text Primary (Light):    #0F172A (Slate 900)
  Text Primary (Dark):     #F8FAFC (Slate 50)
  Text Secondary (Light):  #64748B (Slate 500)
  Text Secondary (Dark):   #A1A1AA (Zinc 400)

Semantic Operational Accents (Strictly Functional):
  P1 / Emergency / Down:   #DC2626 (Red 600)    | Dark: #EF4444 (Red 500)
  P2 / Warning / On Hold:  #D97706 (Amber 600)  | Dark: #F59E0B (Amber 500)
  Operational / Running:   #16A34A (Green 600)  | Dark: #22C55E (Green 500)
  PM / Scheduled / Info:   #2563EB (Blue 600)   | Dark: #3B82F6 (Blue 500)
  Draft / Neutral / Off:   #4B5563 (Gray 600)   | Dark: #71717A (Zinc 500)
```

- **Typography:**
  - UI Chrome & Labels: Crisp, modern geometric sans-serif (`Inter`, `Geist Sans`, or system sans-serif).
  - Data & Metrics: Strictly Monospaced Numerals (`JetBrains Mono`, `Roboto Mono`, or `font-mono tabular-nums`). All Asset Tags (`PMP-0104`), Work Order numbers (`WO-2026-0891`), serial numbers, meter readings, dollar costs, and timestamps must render in fixed-width tabular figures to prevent column jitter and align decimal points.
- **Theme Support:** First-class **High-Contrast Light Mode** (optimized for outdoor sunlight and bright factory bays) and **Industrial Control Room Dark Mode** (deep zinc/slate backgrounds to reduce eye strain during 12-hour night shifts).

#### RECOMMENDATION & DECISION
Adopt a strictly data-driven, utilitarian industrial design language. Enforce standard UI tokens with 1px solid borders, compact row density toggles, tabular monospaced numbers, and zero decorative glassmorphism.

---

# SECTION 3: ENGINEERING ARCHITECTURE & SECURITY PATTERNS

---

### 3.1 QR-Code-Per-Asset Security Architecture & Authorization Boundary

#### EVIDENCE
Physical QR codes printed on durable polyester or anodized aluminum tags are affixed directly to factory machinery. In real-world facilities, these tags are physically accessible to everyone walking the plant floor: authorized maintenance engineers, third-party logistics contractors, temporary cleaning staff, client visitors, and delivery drivers. 

#### FINDING (THE CRITICAL SECURITY CAVEAT)
- **Possession of a QR Code Must NEVER Equal Authorization:** A QR code is simply an alternative keyboard that types a URL into a mobile browser. If an asset URL contains predictable sequential identifiers (e.g. `https://app.cmms.com/assets/4029`), an attacker or contractor can:
  1. Scan one pump tag.
  2. Increment the integer ID in their browser to enumerate and scrape every piece of equipment, proprietary CAD drawing, chemical recipe, vendor price, and downtime record in the entire facility (Insecure Direct Object Reference / IDOR, OWASP API1:2023).
  3. Attempt unauthenticated state changes (e.g. cancelling work orders or faking calibration logs).

#### RECOMMENDATION
Implement a **Dual-Gateway QR Security Architecture**:

```
                  [ Physical QR Tag on Asset ]
                               │
               (Scanned by Camera / Mobile Scanner)
                               │
                               ▼
            URL: https://cmms.company.com/scan/{UUIDv7}
                               │
                               ▼
                  [ Edge Routing Gateway ]
                               │
             ┌─────────────────┴─────────────────┐
             │                                   │
      (Authenticated Session)           (Unauthenticated Session)
             │                                   │
             ▼                                   ▼
    [ Check User RBAC ]                 [ Public Triage Portal ]
             │                                   │
   ┌─────────┴─────────┐                         ▼
   │                   │               - Display ONLY Public Info:
(Tech/Planner)    (No Asset Access)      * Asset Tag (e.g. P-101)
   │                   │                 * Equipment Name
   ▼                   ▼                 * Location (Building B)
[ Full Internal    [ HTTP 403 Forbidden          │
  Asset Details,     Access Denied ]             ▼
  WOs, History,                        - Single Permitted Action:
  Parts & PMs ]                          "Submit Failure Request"
                                         (Upload photo, add note)
                                       - NO Maintenance History
                                       - NO Cost / Financials
                                       - NO Blueprints / Manuals
```

1. **Opaque Identifiers (No Integer Enumeration):** Asset QR codes embed cryptographically random, collision-resistant UUIDv7 identifiers (e.g., `/scan/018db23a-7f12-70b1-912a-84a1e508fb10`). Sequential auto-increment integer IDs are strictly forbidden in public URLs.
2. **Strict RBAC Decoupling:**
   - If the request originates from an **Authenticated Session** (logged into the mobile app or web browser), the system verifies the user's role and Tenant/Location permissions before rendering internal work orders, maintenance logs, or schematics.
   - If the request originates from an **Unauthenticated Session** (e.g. an operator using their personal phone camera), the gateway routes to a public, sandboxed **Issue Intake Form**. This form displays *only* minimal, non-sensitive public metadata (`Asset Tag`, `Equipment Name`, `Location Name`) and provides a single action: "Report Problem / Request Maintenance".
3. **Rate Limiting:** Public scan intake endpoints are protected by IP and subnet rate limiting (e.g., maximum 10 requests per minute per IP) to prevent automated dictionary attacks.

#### DECISION
Adopt the Dual-Gateway QR pattern using opaque UUIDv7 identifiers. Public scans render a sandboxed, low-privilege issue reporting portal; full asset details and maintenance history require verified tenant authentication and role authorization.

---

### 3.2 Attachment & File-Upload Security Architecture

#### EVIDENCE
Industrial maintenance workflows generate substantial unstructured file data:
- Technicians upload before-and-after photos of failed bearings and burnt motor windings.
- Reliability engineers upload multi-megabyte equipment O&M manuals (PDFs) and wiring schematics.
- Technicians upload short audio/video clips of abnormal machine sounds or thermal camera captures.

Unrestricted file uploads (OWASP Top 10, CWE-434) expose systems to Remote Code Execution (RCE), Cross-Site Scripting (XSS via malicious SVGs), Denial of Service (zip bombs, multi-gigabyte uploads saturating web servers), and Server-Side Request Forgery (SSRF).

#### FINDING
- Allowing browser uploads to pass directly through the core web application server exhausts web server worker threads, bloats memory buffers, and exposes the application to path-traversal exploits (`../../etc/passwd`).
- Client-supplied `Content-Type` headers and file extensions are easily spoofed (e.g., an executable `.exe` or malicious `.php` script renamed to `motor_nameplate.jpg`).
- SVG files are XML documents capable of executing arbitrary JavaScript when rendered directly in the DOM, creating severe Stored XSS vulnerabilities.

#### RECOMMENDATION
Implement a **Direct-to-Storage Presigned Upload Pipeline with Multi-Layer Sanitization**:

```
[ Client Browser / PWA ]            [ CMMS API Server ]            [ Object Storage (S3/Blob) ]
         │                                   │                                  │
         │ 1. POST /attachments/presign      │                                  │
         │    (FileName, Size, MIME, AssetId)│                                  │
         ├──────────────────────────────────►│                                  │
         │                                   │ 2. Validate Tenant, Size,        │
         │                                   │    Extension & Generate UUID Key │
         │                                   │    (tenants/{id}/temp/{uuid})    │
         │ 3. Return Presigned PUT URL       │                                  │
         │◄──────────────────────────────────┤                                  │
         │                                                                      │
         │ 4. Direct HTTP PUT Binary Stream (Bypasses API server entirely)      │
         ├─────────────────────────────────────────────────────────────────────►│
         │                                                                      │
         │ 5. POST /attachments/confirm                                         │
         ├──────────────────────────────────►│                                  │
         │                                   │ 6. Trigger Asynchronous          │
         │                                   │    Validation Job                │
         │                                   │    (Hangfire / Worker)           │
         │                                   │           │                      │
         │                                   │           ▼                      │
         │                                   │    [ Validation Worker ]         │
         │                                   │    - Magic Byte Verification     │
         │                                   │    - Strip EXIF GPS Data         │
         │                                   │    - Re-encode Images (JPEG/WebP)│
         │                                   │    - Move to Permanent Storage   │
         │ 7. Return Verified Attachment DTO │◄──────────┘                      │
         │◄──────────────────────────────────┤                                  │
```

1. **Presigned Cloud Storage Uploads:** Technicians upload directly to cloud object storage (AWS S3, Azure Blob, or MinIO) via short-lived (15-minute) presigned PUT URLs generated by the API. The API application server never buffers raw file uploads.
2. **Content-Length & Type Policy in Presigned Signature:** Enforce strict file size limits directly inside the cloud storage presigned policy (e.g., Photos: max 15MB; Equipment Manuals: max 50MB).
3. **Magic Byte Verification (True MIME-Type Inspection):** An asynchronous worker inspects the initial bytes of the uploaded file (magic numbers) to verify that a file claiming to be a JPEG or PNG actually conforms to binary image standards, discarding spoofed extensions.
4. **Active Content Neutralization (SVG Ban):** SVGs are strictly rejected for maintenance photo uploads. For document schematics, if vector drawings are required, they are served exclusively as raw downloadable attachments with hard security headers:
   `Content-Disposition: attachment; filename="schematic.pdf"`  
   `Content-Security-Policy: default-src 'none'`  
   `X-Content-Type-Options: nosniff`
5. **Storage Isolation & Key Randomization:** Files are never stored under user-supplied names. Storage keys are strictly randomized and tenant-partitioned:
   `s3://cmms-storage/tenants/{tenant_id}/assets/{asset_id}/{uuidv7}.{verified_extension}`
6. **Asynchronous Malware & Antivirus Scanning:** Background workers pass uploaded objects through an antivirus scanner (e.g., ClamAV daemon) before updating the attachment status to `Active`.

#### DECISION
Adopt the Presigned Direct-to-Storage upload architecture. Enforce strict magic-byte validation on background workers, isolate tenant storage paths, and strictly serve attachments with download/nosniff security headers.

---

### 3.3 Background Job Scheduling Patterns for Recurring Preventive Maintenance

#### EVIDENCE
In production enterprise environments, CMMS application servers run in multi-instance configurations (e.g., 2 to 6 clustered container pods behind a load balancer for high availability). 

#### FINDING (THE MULTI-INSTANCE CONCURRENCY BUG)
If preventive maintenance generation relies on naive in-memory timers (`System.Threading.Timer`, `setInterval`, or standard local cron jobs), each running container instance will simultaneously detect that a PM plan is due at 00:00 UTC. This results in **duplicate Work Orders**: two or three identical work orders are inserted into the database, generating multiple duplicate notifications to technicians and double-booking maintenance capacity.

#### RECOMMENDATION
1. **Persistent Distributed Job Orchestration:**
   - Utilize an enterprise distributed job scheduler backed by persistent database storage (such as **Hangfire** or **Quartz.NET** using PostgreSQL / SQL Server storage tables).
   - Distributed schedulers leverage database-backed distributed locks and transactional state tables, ensuring that exactly one worker node acquires the lock to execute the recurrence evaluation sweep.
2. **Database-Level Idempotency Guarantees:**
   - Even if scheduler locks fail or network partitions occur, the database schema must make duplicate generation impossible.
   - Maintain a dedicated tracking table: `PreventiveGenerationHistory` with a composite unique constraint:
     $$\text{UNIQUE}(\text{MaintenancePlanId}, \text{ScheduledDueDate})$$
   - The generation transaction executes an idempotent upsert:
     ```sql
     INSERT INTO work_orders (id, maintenance_plan_id, asset_id, status, scheduled_date, ...)
     SELECT gen_random_uuid(), plan.id, plan.asset_id, 'SCHEDULED', :target_due_date, ...
     WHERE NOT EXISTS (
         SELECT 1 FROM preventive_generation_history 
         WHERE maintenance_plan_id = plan.id AND scheduled_due_date = :target_due_date
     );
     ```
3. **Timezone-Aware Schedule Evaluation:**
   - Industrial plants operate across different geographic regions. A plant in São Paulo (`UTC-3`) expects daily PM work orders generated for the 06:00 morning shift, while a plant in Chicago (`UTC-6`) expects them at their local 06:00.
   - Every `FunctionalLocation` / `Site` defines its local IANA timezone (e.g. `America/Sao_Paulo`, `America/Chicago`).
   - The scheduler stores all internal execution logs in UTC, but evaluates cron expressions against the plant's local timezone.

#### DECISION
Implement recurring PM generation via a distributed, database-backed scheduler (Hangfire / Quartz.NET) paired with database-level composite unique idempotency constraints `(maintenance_plan_id, scheduled_due_date)` and IANA timezone-aware recurrence evaluation.

---

### 3.4 Technology Justification Audit: SignalR & OpenTelemetry

#### EVIDENCE
Engineering teams frequently suffer from "technology bingo"—adopting real-time protocols and complex telemetry frameworks without clear operational justification, resulting in unnecessary infrastructure costs, connection stability issues, and elevated code complexity.

#### ARCHITECTURAL AUDIT & FINDINGS

#### 1. SignalR (WebSockets) Justification Audit
- **The Case Against Full-Duplex Sockets for Everything:**
  - A CMMS is not a high-frequency trading terminal or an SCADA telemetry dashboard. A pump's maintenance record or a technician's work order does not change state every 100 milliseconds. 
  - Holding persistent WebSocket connections open across 500 mobile technician tablets roaming through factory dead zones causes constant connection drops, reconnect storms, battery drain, and memory leaks.
  - Standard REST/HTTP APIs with client-side caching (e.g., TanStack React Query with `refetchOnWindowFocus` and a 30-second stale time) solve 90% of data synchronization needs with zero connection overhead.
- **The Justified Operational Scope for SignalR:**
  - A persistent real-time push channel is justified *specifically* for the **Central Dispatch & Control Room Board**:
    1. **Emergency P1 Breakdown Alerts:** When an operator on the line logs an emergency line-stop failure, supervisors and dispatchers on the web console must see an immediate banner alert without waiting for a manual refresh.
    2. **Concurrent Dispatch Collision Prevention:** When two dispatchers are viewing the backlog, if Dispatcher A assigns Work Order #402 to Technician Bob, Dispatcher B's screen must immediately reflect the assignment to prevent duplicate dispatching.
- **Recommendation:** Implement a bounded, lightweight SignalR Hub (`/hubs/dispatch`) dedicated exclusively to supervisor dispatch board events and high-priority emergency notifications. Standard asset lists, checklists, parts inventories, and reports shall use standard HTTP caching with stale-while-revalidate policies.

#### 2. OpenTelemetry (Observability) Justification Audit
- **The Justification for OpenTelemetry:**
  - Unlike simple web applications, a CMMS executes complex multi-step asynchronous distributed workflows:
    - User scans QR code $\rightarrow$ REST API request $\rightarrow$ recursive CTE database query traversing 6 levels of functional locations $\rightarrow$ returns asset data.
    - Technician uploads failure photo $\rightarrow$ direct S3 upload $\rightarrow$ storage webhook triggers background validation worker $\rightarrow$ image magic-bytes checked $\rightarrow$ thumbnail generated.
    - Nightly background scheduler evaluates 10,000 maintenance plans $\rightarrow$ acquires distributed lock $\rightarrow$ evaluates cron expressions $\rightarrow$ creates batch work orders $\rightarrow$ sends email notifications.
  - When a plant manager reports: *"Our morning PM work orders didn't generate today"* or *"The asset tree takes 4 seconds to load during shift change"*, basic server log files across multiple Docker containers require painful, manual correlation.
  - **OpenTelemetry (OTel)** provides vendor-neutral distributed tracing (`traceparent` header propagation across HTTP requests, background Hangfire jobs, and database queries). It allows instant visualization of database query bottlenecks (e.g. unindexed hierarchical queries) and scheduler latencies.
  - In modern .NET / ASP.NET Core, OpenTelemetry is natively supported through `System.Diagnostics.Activity` and `ILogger` with near-zero runtime CPU overhead. Traces and metrics export cleanly to open-source stacks (Prometheus, Grafana Tempo, Jaeger) or commercial platforms (Datadog, Dynatrace) without vendor lock-in.

#### RECOMMENDATION & DECISION
1. **SignalR:** **APPROVED (Bounded Scope).** Adopt SignalR strictly for the Dispatch Board and Emergency P1 alerts. Forbid universal full-duplex socket syncing for static tables and forms.
2. **OpenTelemetry:** **APPROVED (Core Infrastructure).** Adopt OpenTelemetry from Day 1 for distributed tracing across the API gateway, background job scheduler, and database query pipeline.

---

# SECTION 4: M0 ARCHITECTURAL DECISION RECORDS (ADR SUMMARY)

| ADR ID | Architecture Domain | Architectural Decision | Primary Rationale & Justification |
| :--- | :--- | :--- | :--- |
| **ADR-01** | Domain Model | **Two-Stage Maintenance Request $\rightarrow$ Work Order Separation** | Eliminates plant floor ticket spam; protects backlog metrics from unvetted operator submissions. |
| **ADR-02** | Domain Model | **Dual Hierarchy: Functional Locations vs Physical Assets** | Complies with ISO 14224/ISA-95; allows rotating spares (pumps, motors) to move without corrupting location history. |
| **ADR-03** | Domain Model | **Dual Calendar (Fixed/Floating) & Meter Scheduling Engine** | Prevents PM stacking via explicit suppression policies; supports lead-time generation offset for parts staging. |
| **ADR-04** | Domain Metrics | **Mathematically Defensible MTBF/MTTR/Backlog Formulas** | Standardizes on SMRP/ISO 14224 definitions; strictly decouples wrench time from total machine downtime. |
| **ADR-05** | UI/UX System | **Utilitarian Industrial Design (Anti-Purple/Anti-Glassmorphism)** | Maximizes information density and daylight visibility; replaces AI gimmicks with 1px borders and tabular monospaced figures. |
| **ADR-06** | UI/UX System | **Dual-Persona Interface (Desktop Console vs Technician Mobile)** | Serves high-density multi-tasking planners on desktop while giving technicians glove-friendly, 1-tap mobile execution. |
| **ADR-07** | Security | **Opaque QR Codes with Dual-Gateway Authorization Boundary** | Mitigates IDOR enumeration; physical QR possession never equates to authenticated access. |
| **ADR-08** | Security | **Presigned Direct-to-Storage Uploads with Magic Byte Inspection** | Protects API servers from bandwidth exhaustion; neutralizes SVG/malicious executable attack vectors. |
| **ADR-09** | Engineering | **Distributed Scheduler with Unique Database Idempotency Keys** | Prevents duplicate PM work order generation across clustered multi-replica container nodes. |
| **ADR-10** | Engineering | **Bounded SignalR for Dispatch + Day-1 OpenTelemetry Instrumentation** | Avoids WebSocket connection bloat while securing real-time emergency dispatch and deep distributed observability. |

---
*Document compiled and approved for M0 Architecture Kickoff.*  
*Ready for Milestone 1: Domain Entity Modeling, Database Schema Migration & Core API Scaffolding.*
