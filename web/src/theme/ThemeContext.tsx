import { createContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import {
  THEME_STORAGE_KEY,
  getSystemTheme,
  readStoredPreference,
  type ResolvedTheme,
  type ThemePreference,
} from './theme'

export interface ThemeContextValue {
  preference: ThemePreference
  resolved: ResolvedTheme
  setPreference: (pref: ThemePreference) => void
}

export const ThemeContext = createContext<ThemeContextValue | null>(null)

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [preference, setPreference] = useState<ThemePreference>(readStoredPreference)
  // Only genuinely external state: the OS-level color scheme. Everything
  // else (resolved theme) is derived from this plus `preference` below.
  const [systemDark, setSystemDark] = useState(getSystemTheme() === 'dark')

  const resolved: ResolvedTheme = preference === 'system' ? (systemDark ? 'dark' : 'light') : preference

  // Keep <html data-theme> in sync. The inline script in index.html already
  // set the correct value pre-paint; this only re-applies it on change.
  useEffect(() => {
    document.documentElement.setAttribute('data-theme', resolved)
  }, [resolved])

  useEffect(() => {
    try {
      localStorage.setItem(THEME_STORAGE_KEY, preference)
    } catch {
      // ignore write failures
    }
  }, [preference])

  useEffect(() => {
    // "System" stays live: if the OS theme flips while the app is open, the
    // UI follows without a reload.
    const media = window.matchMedia('(prefers-color-scheme: dark)')
    const onChange = () => setSystemDark(media.matches)
    media.addEventListener('change', onChange)
    return () => media.removeEventListener('change', onChange)
  }, [])

  const value = useMemo(() => ({ preference, resolved, setPreference }), [preference, resolved])

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>
}
