import { useEffect, useState } from 'react'

/** useState backed by localStorage — used for UI prefs like sidebar collapse, not domain data. */
export function usePersistentState<T>(key: string, initial: T) {
  const [value, setValue] = useState<T>(() => {
    try {
      const stored = localStorage.getItem(key)
      return stored !== null ? (JSON.parse(stored) as T) : initial
    } catch {
      return initial
    }
  })

  useEffect(() => {
    try {
      localStorage.setItem(key, JSON.stringify(value))
    } catch {
      // ignore write failures (private browsing, quota)
    }
  }, [key, value])

  return [value, setValue] as const
}
