import '@testing-library/jest-dom/vitest'
import { afterEach, beforeEach } from 'vitest'
import { installMatchMediaMock } from './mockMatchMedia'

// jsdom has no real matchMedia; every test that mounts ThemeProvider needs
// one. Tests that care about a specific OS preference (or the live-change
// listener) call installMatchMediaMock again themselves, which overrides
// this default.
beforeEach(() => {
  installMatchMediaMock(false)
})

// Theme preference / sidebar-collapse persistence would otherwise leak
// between tests via jsdom's real localStorage implementation.
afterEach(() => {
  localStorage.clear()
  document.documentElement.removeAttribute('data-theme')
})
