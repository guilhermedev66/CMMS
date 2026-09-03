import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it } from 'vitest'
import { installMatchMediaMock } from '../test/mockMatchMedia'
import { THEME_STORAGE_KEY } from './theme'
import { ThemeProvider } from './ThemeContext'
import { useTheme } from './useTheme'

function ThemeConsumer() {
  const { preference, resolved, setPreference } = useTheme()
  return (
    <div>
      <span data-testid="preference">{preference}</span>
      <span data-testid="resolved">{resolved}</span>
      <button onClick={() => setPreference('light')}>set-light</button>
      <button onClick={() => setPreference('dark')}>set-dark</button>
      <button onClick={() => setPreference('system')}>set-system</button>
    </div>
  )
}

describe('ThemeProvider / useTheme', () => {
  beforeEach(() => {
    installMatchMediaMock(false) // system defaults to light unless a test says otherwise
  })

  it('defaults to "system" with no persisted preference, and applies data-theme', () => {
    render(
      <ThemeProvider>
        <ThemeConsumer />
      </ThemeProvider>,
    )

    expect(screen.getByTestId('preference')).toHaveTextContent('system')
    expect(screen.getByTestId('resolved')).toHaveTextContent('light')
    expect(document.documentElement.getAttribute('data-theme')).toBe('light')
  })

  it('resolves an explicit persisted preference on initial render, without waiting for an interaction', () => {
    localStorage.setItem(THEME_STORAGE_KEY, 'dark')

    render(
      <ThemeProvider>
        <ThemeConsumer />
      </ThemeProvider>,
    )

    expect(screen.getByTestId('preference')).toHaveTextContent('dark')
    expect(screen.getByTestId('resolved')).toHaveTextContent('dark')
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
  })

  it('ignores a corrupted/unknown persisted value and falls back to "system"', () => {
    localStorage.setItem(THEME_STORAGE_KEY, 'purple-mode')

    render(
      <ThemeProvider>
        <ThemeConsumer />
      </ThemeProvider>,
    )

    expect(screen.getByTestId('preference')).toHaveTextContent('system')
  })

  it('setting a preference updates resolved theme, the DOM attribute, and persists to localStorage', async () => {
    const user = userEvent.setup()
    render(
      <ThemeProvider>
        <ThemeConsumer />
      </ThemeProvider>,
    )

    await user.click(screen.getByText('set-dark'))

    expect(screen.getByTestId('preference')).toHaveTextContent('dark')
    expect(screen.getByTestId('resolved')).toHaveTextContent('dark')
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark')

    await user.click(screen.getByText('set-light'))

    expect(screen.getByTestId('resolved')).toHaveTextContent('light')
    expect(document.documentElement.getAttribute('data-theme')).toBe('light')
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('light')
  })

  it('in "system" mode, follows a live prefers-color-scheme change without an explicit setPreference call', async () => {
    const media = installMatchMediaMock(false)
    const user = userEvent.setup()

    render(
      <ThemeProvider>
        <ThemeConsumer />
      </ThemeProvider>,
    )

    expect(screen.getByTestId('resolved')).toHaveTextContent('light')

    // Simulate the OS flipping to dark while "System" is selected.
    act(() => media.setMatches(true))

    expect(screen.getByTestId('resolved')).toHaveTextContent('dark')
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark')

    // Switching off "system" must stop following OS changes.
    await user.click(screen.getByText('set-light'))
    act(() => media.setMatches(false))
    act(() => media.setMatches(true))

    expect(screen.getByTestId('resolved')).toHaveTextContent('light')
  })
})
