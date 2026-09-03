import { Monitor, Moon, Sun, type LucideIcon } from 'lucide-react'
import type { ThemePreference } from './theme'
import { useTheme } from './useTheme'

const options: { value: ThemePreference; label: string; icon: LucideIcon }[] = [
  { value: 'light', label: 'Light', icon: Sun },
  { value: 'system', label: 'System', icon: Monitor },
  { value: 'dark', label: 'Dark', icon: Moon },
]

export function ThemeToggle() {
  const { preference, setPreference } = useTheme()

  return (
    <div role="group" aria-label="Theme" className="flex items-center gap-0.5 rounded-md border border-border p-0.5">
      {options.map(({ value, label, icon: Icon }) => (
        <button
          key={value}
          type="button"
          aria-pressed={preference === value}
          title={label}
          onClick={() => setPreference(value)}
          className={`flex h-7 w-7 items-center justify-center rounded-sm transition-colors ${
            preference === value
              ? 'bg-accent text-accent-contrast'
              : 'text-text-secondary hover:bg-surface hover:text-text-primary'
          }`}
        >
          <Icon className="h-[15px] w-[15px]" strokeWidth={1.75} />
          <span className="sr-only">{label}</span>
        </button>
      ))}
    </div>
  )
}
