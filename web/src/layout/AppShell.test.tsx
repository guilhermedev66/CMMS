import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../App'
import { authenticatedFixture, renderWithProviders } from '../test/renderWithProviders'

describe('AppShell (via App routing)', () => {
  beforeEach(() => {
    // AssetsListPage fetches on mount; these tests exercise shell/routing
    // mechanics, not that data, so stub it to an empty-but-successful response.
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(new Response('[]', { status: 200, headers: { 'Content-Type': 'application/json' } })),
    )
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('renders the sidebar, top bar, and the real Dashboard page on the index route', () => {
    renderWithProviders(<App />, { auth: authenticatedFixture })

    expect(screen.getByRole('link', { name: /dashboard/i })).toBeInTheDocument()
    expect(screen.getByPlaceholderText(/search assets, work orders/i)).toBeInTheDocument()
    // The fixture user is a Planner, so DashboardPage renders the Planner/Admin view (M5).
    expect(screen.getByRole('heading', { name: 'Operations Dashboard' })).toBeInTheDocument()
  })

  it('navigating to a nav item swaps the routed content while the shell stays mounted', async () => {
    const user = userEvent.setup()
    renderWithProviders(<App />, { auth: authenticatedFixture })

    await user.click(screen.getByRole('link', { name: /assets/i }))

    expect(screen.getByRole('heading', { name: 'Assets' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Operations Dashboard' })).not.toBeInTheDocument()
    // Shell chrome persists across the route change.
    expect(screen.getByRole('link', { name: /work orders/i })).toBeInTheDocument()
  })

  it('collapsing the sidebar from within the full shell still hides labels but keeps routing usable', async () => {
    const user = userEvent.setup()
    renderWithProviders(<App />, { auth: authenticatedFixture })

    await user.click(screen.getByRole('button', { name: 'Collapse sidebar' }))
    expect(screen.queryByText('Assets')).not.toBeInTheDocument()

    // The nav link is still reachable (by icon) with an accessible title even collapsed.
    const links = screen.getAllByRole('link')
    const assetsLink = links.find((link) => link.getAttribute('title') === 'Assets')
    expect(assetsLink).toBeDefined()

    await user.click(assetsLink!)
    expect(screen.getByRole('heading', { name: 'Assets' })).toBeInTheDocument()
  })

  it('redirects to /login when unauthenticated, and shows the login form', () => {
    renderWithProviders(<App />, { auth: { status: 'unauthenticated', user: null } })

    expect(screen.getByRole('heading', { name: 'Sign in' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /dashboard/i })).not.toBeInTheDocument()
  })
})
