import { ChevronsLeft, ChevronsRight } from 'lucide-react'
import { NavLink } from 'react-router-dom'
import { usePersistentState } from '../hooks/usePersistentState'
import { navItems } from '../nav'

export function Sidebar() {
  const [collapsed, setCollapsed] = usePersistentState('cmms-sidebar-collapsed', false)

  return (
    <aside
      className={`flex shrink-0 flex-col border-r border-border bg-surface-raised transition-[width] duration-150 ${
        collapsed ? 'w-16' : 'w-60'
      }`}
    >
      <div className="flex h-14 items-center gap-2 border-b border-border px-4">
        <div className="flex h-7 w-7 shrink-0 items-center justify-center rounded-sm bg-accent font-mono text-sm font-semibold text-accent-contrast">
          C
        </div>
        {!collapsed && <span className="truncate text-sm font-semibold">CMMS</span>}
      </div>

      <nav className="flex-1 space-y-0.5 overflow-y-auto p-2">
        {navItems.map(({ path, label, icon: Icon }) => (
          <NavLink
            key={path}
            to={path}
            end={path === '/'}
            title={collapsed ? label : undefined}
            className={({ isActive }) =>
              `flex items-center gap-3 rounded-sm px-2.5 py-2 text-sm transition-colors ${
                isActive
                  ? 'bg-accent/10 font-medium text-accent'
                  : 'text-text-secondary hover:bg-surface hover:text-text-primary'
              }`
            }
          >
            <Icon className="h-[18px] w-[18px] shrink-0" strokeWidth={1.75} />
            {!collapsed && <span className="truncate">{label}</span>}
          </NavLink>
        ))}
      </nav>

      <button
        type="button"
        onClick={() => setCollapsed(!collapsed)}
        aria-label={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
        className="flex items-center gap-2 border-t border-border px-4 py-3 text-sm text-text-secondary hover:bg-surface hover:text-text-primary"
      >
        {collapsed ? <ChevronsRight className="h-4 w-4" /> : <ChevronsLeft className="h-4 w-4" />}
        {!collapsed && <span>Collapse</span>}
      </button>
    </aside>
  )
}
