import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { navItems } from '../nav'
import { renderWithProviders } from '../test/renderWithProviders'
import { Sidebar } from './Sidebar'

describe('Sidebar', () => {
  it('renders every nav item from the config', () => {
    renderWithProviders(<Sidebar />)

    for (const item of navItems) {
      expect(screen.getByText(item.label)).toBeInTheDocument()
    }
  })

  it('marks the current route active via aria-current, and no others', () => {
    renderWithProviders(<Sidebar />, { route: '/assets' })

    expect(screen.getByRole('link', { name: /assets/i })).toHaveAttribute('aria-current', 'page')
    expect(screen.getByRole('link', { name: /dashboard/i })).not.toHaveAttribute('aria-current')
    expect(screen.getByRole('link', { name: /work orders/i })).not.toHaveAttribute('aria-current')
  })

  it('collapses to icon-only on click, hiding labels, and expands back on a second click', async () => {
    const user = userEvent.setup()
    renderWithProviders(<Sidebar />)

    expect(screen.getByText('Dashboard')).toBeInTheDocument()
    const collapseButton = screen.getByRole('button', { name: 'Collapse sidebar' })

    await user.click(collapseButton)

    expect(screen.queryByText('Dashboard')).not.toBeInTheDocument()
    expect(screen.queryByText('Assets')).not.toBeInTheDocument()
    const expandButton = screen.getByRole('button', { name: 'Expand sidebar' })

    await user.click(expandButton)

    expect(screen.getByText('Dashboard')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Collapse sidebar' })).toBeInTheDocument()
  })

  it('persists the collapsed state to localStorage', async () => {
    const user = userEvent.setup()
    renderWithProviders(<Sidebar />)

    await user.click(screen.getByRole('button', { name: 'Collapse sidebar' }))

    expect(localStorage.getItem('cmms-sidebar-collapsed')).toBe('true')

    await user.click(screen.getByRole('button', { name: 'Expand sidebar' }))

    expect(localStorage.getItem('cmms-sidebar-collapsed')).toBe('false')
  })
})
