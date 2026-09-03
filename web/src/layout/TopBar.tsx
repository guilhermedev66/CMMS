import { LogOut, Search, User } from 'lucide-react'
import { useState } from 'react'
import { useAuth } from '../auth/useAuth'
import { ThemeToggle } from '../theme/ThemeToggle'

export function TopBar() {
  const { user, logout } = useAuth()
  const [signingOut, setSigningOut] = useState(false)

  async function handleLogout() {
    setSigningOut(true)
    try {
      await logout()
    } finally {
      setSigningOut(false)
    }
  }

  return (
    <header className="flex h-14 shrink-0 items-center gap-3 border-b border-border bg-surface-raised px-4">
      <div className="relative max-w-md flex-1">
        <Search
          className="pointer-events-none absolute top-1/2 left-2.5 h-4 w-4 -translate-y-1/2 text-text-secondary"
          strokeWidth={1.75}
        />
        <input
          type="search"
          placeholder="Search assets, work orders… (coming in M2)"
          disabled
          className="w-full rounded-sm border border-border bg-surface py-1.5 pr-3 pl-8 text-sm text-text-primary placeholder:text-text-secondary disabled:cursor-not-allowed disabled:opacity-70"
        />
      </div>

      <button
        type="button"
        title="Site switching lands with multi-site support"
        disabled
        className="hidden items-center gap-1.5 rounded-sm border border-border px-2.5 py-1.5 text-sm text-text-secondary disabled:cursor-not-allowed disabled:opacity-70 sm:flex"
      >
        All Sites
      </button>

      <ThemeToggle />

      <button
        type="button"
        title={signingOut ? 'Signing out…' : 'Log out'}
        onClick={handleLogout}
        disabled={signingOut}
        className="flex items-center gap-2 rounded-sm border border-border px-2 py-1.5 text-sm text-text-secondary hover:border-border-strong hover:text-text-primary disabled:cursor-not-allowed disabled:opacity-70"
      >
        <User className="h-4 w-4" strokeWidth={1.75} />
        <span className="hidden max-w-[12ch] truncate sm:inline">{user?.email ?? 'Account'}</span>
        <LogOut className="h-3.5 w-3.5" strokeWidth={1.75} />
      </button>
    </header>
  )
}
