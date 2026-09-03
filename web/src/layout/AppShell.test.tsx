import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import App from '../App'
import { renderWithProviders } from '../test/renderWithProviders'

describe('AppShell (via App routing)', () => {
  it('renders the sidebar, top bar, and the Dashboard placeholder on the index route', () => {
    renderWithProviders(<App />)

    expect(screen.getByRole('link', { name: /dashboard/i })).toBeInTheDocument()
    expect(screen.getByPlaceholderText(/search assets, work orders/i)).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()
  })

  it('navigating to a nav item swaps the routed content while the shell stays mounted', async () => {
    const user = userEvent.setup()
    renderWithProviders(<App />)

    await user.click(screen.getByRole('link', { name: /assets/i }))

    expect(screen.getByRole('heading', { name: 'Assets' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Dashboard' })).not.toBeInTheDocument()
    // Shell chrome persists across the route change.
    expect(screen.getByRole('link', { name: /work orders/i })).toBeInTheDocument()
  })

  it('collapsing the sidebar from within the full shell still hides labels but keeps routing usable', async () => {
    const user = userEvent.setup()
    renderWithProviders(<App />)

    await user.click(screen.getByRole('button', { name: 'Collapse sidebar' }))
    expect(screen.queryByText('Assets')).not.toBeInTheDocument()

    // The nav link is still reachable (by icon) with an accessible title even collapsed.
    const links = screen.getAllByRole('link')
    const assetsLink = links.find((link) => link.getAttribute('title') === 'Assets')
    expect(assetsLink).toBeDefined()

    await user.click(assetsLink!)
    expect(screen.getByRole('heading', { name: 'Assets' })).toBeInTheDocument()
  })
})
