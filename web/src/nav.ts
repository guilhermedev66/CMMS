import {
  BarChart3,
  Boxes,
  CalendarDays,
  ClipboardList,
  Inbox,
  LayoutDashboard,
  Settings as SettingsIcon,
  type LucideIcon,
} from 'lucide-react'

export interface NavItem {
  path: string
  label: string
  icon: LucideIcon
  description: string
  milestone: string
}

// Single source of truth for the left rail and the routed placeholder pages
// (see App.tsx) — matches the nav structure and screen list in
// docs/04-frontend-ia.md.
export const navItems: NavItem[] = [
  {
    path: '/',
    label: 'Dashboard',
    icon: LayoutDashboard,
    description: 'Role-aware KPI ribbon and an attention-needed list — no decorative chart wall.',
    milestone: 'Wired up in M5 — Reporting & Operations.',
  },
  {
    path: '/assets',
    label: 'Assets',
    icon: Boxes,
    description: 'Asset registry with location hierarchy, criticality, and Work Order history.',
    milestone: 'Data wiring is next, on top of the Assets API landing in this M1 slice.',
  },
  {
    path: '/requests',
    label: 'Requests',
    icon: Inbox,
    description: 'Maintenance request intake and Planner conversion to Work Orders.',
    milestone: 'Wired up in M2 — Requests & Work Orders.',
  },
  {
    path: '/work-orders',
    label: 'Work Orders',
    icon: ClipboardList,
    description: 'Grid and Kanban views across the guarded Work Order lifecycle.',
    milestone: 'Wired up in M2 — Requests & Work Orders.',
  },
  {
    path: '/planning',
    label: 'Planning',
    icon: CalendarDays,
    description: 'Preventive maintenance calendar — month, week, and agenda views.',
    milestone: 'Wired up in M3 — Preventive Maintenance.',
  },
  {
    path: '/reports',
    label: 'Reports',
    icon: BarChart3,
    description: 'MTBF, MTTR, availability, backlog, and cost reporting — table-first, export-ready.',
    milestone: 'Wired up in M5 — Reporting & Operations.',
  },
  {
    path: '/settings',
    label: 'Settings',
    icon: SettingsIcon,
    description: 'Users, sites, asset categories, and checklist templates.',
    milestone: 'Wired up alongside Identity + RBAC and later modules.',
  },
]
