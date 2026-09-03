import { vi } from 'vitest'

type Listener = (event: { matches: boolean }) => void

/**
 * jsdom has no real `matchMedia`. This installs a controllable fake for
 * `(prefers-color-scheme: dark)` so tests can assert both the initial read
 * and the live-listener path (see ThemeContext's "System" mode) without a
 * real OS theme change.
 */
export function installMatchMediaMock(initialMatches = false) {
  let matches = initialMatches
  const listeners = new Set<Listener>()

  window.matchMedia = vi.fn().mockImplementation((query: string) => ({
    media: query,
    get matches() {
      return matches
    },
    addEventListener: (_event: string, cb: Listener) => listeners.add(cb),
    removeEventListener: (_event: string, cb: Listener) => listeners.delete(cb),
    // Deprecated but still checked by some libraries; harmless to support.
    addListener: (cb: Listener) => listeners.add(cb),
    removeListener: (cb: Listener) => listeners.delete(cb),
    dispatchEvent: () => true,
  }))

  return {
    get matches() {
      return matches
    },
    get listenerCount() {
      return listeners.size
    },
    setMatches(value: boolean) {
      matches = value
      listeners.forEach((cb) => cb({ matches: value }))
    },
  }
}
