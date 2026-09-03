import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it } from 'vitest'
import { installMatchMediaMock } from '../test/mockMatchMedia'
import { THEME_STORAGE_KEY } from './theme'
import { ThemeProvider } from './ThemeContext'
import { ThemeToggle } from './ThemeToggle'

function renderToggle() {
  return render(
    <ThemeProvider>
      <ThemeToggle />
    </ThemeProvider>,
  )
}

describe('ThemeToggle', () => {
  beforeEach(() => {
    installMatchMediaMock(false)
  })

  it('renders Light, System, and Dark options, with System pressed by default', () => {
    renderToggle()

    const light = screen.getByRole('button', { name: 'Light' })
    const system = screen.getByRole('button', { name: 'System' })
    const dark = screen.getByRole('button', { name: 'Dark' })

    expect(light).toHaveAttribute('aria-pressed', 'false')
    expect(system).toHaveAttribute('aria-pressed', 'true')
    expect(dark).toHaveAttribute('aria-pressed', 'false')
  })

  it('cycles Light -> Dark -> System, updating aria-pressed, <html data-theme>, and localStorage each step', async () => {
    const user = userEvent.setup()
    renderToggle()

    const light = screen.getByRole('button', { name: 'Light' })
    const system = screen.getByRole('button', { name: 'System' })
    const dark = screen.getByRole('button', { name: 'Dark' })

    await user.click(light);
    expect(light).toHaveAttribute('aria-pressed', 'true')
    expect(dark).toHaveAttribute('aria-pressed', 'false')
    expect(document.documentElement.getAttribute('data-theme')).toBe('light')
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('light')

    await user.click(dark);
    expect(dark).toHaveAttribute('aria-pressed', 'true')
    expect(light).toHaveAttribute('aria-pressed', 'false')
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark')

    await user.click(system);
    expect(system).toHaveAttribute('aria-pressed', 'true')
    expect(dark).toHaveAttribute('aria-pressed', 'false')
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('system')
    // System was mocked to prefers-color-scheme: light, so resolving to
    // "system" here should land back on light.
    expect(document.documentElement.getAttribute('data-theme')).toBe('light')
  })

  it('resolves System to dark when the OS preference is dark', async () => {
    const media = installMatchMediaMock(true)
    const user = userEvent.setup()
    renderToggle()

    await user.click(screen.getByRole('button', { name: 'Dark' }))
    await user.click(screen.getByRole('button', { name: 'System' }))

    expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
    expect(media.matches).toBe(true)
  })
})
