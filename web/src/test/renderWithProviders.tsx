import { render } from '@testing-library/react'
import type { ReactElement, ReactNode } from 'react'
import { MemoryRouter } from 'react-router-dom'
import { vi } from 'vitest'
import { AuthContext, type AuthContextValue } from '../auth/AuthContext'
import { ThemeProvider } from '../theme/ThemeContext'

export interface RenderOptions {
  route?: string
  /**
   * Provide a fake AuthContext value instead of mounting the real
   * AuthProvider (which would hit the network). Omit for tests that don't
   * touch anything auth-aware; pass a value (e.g. an authenticated user) for
   * anything that renders behind ProtectedRoute or reads useAuth().
   */
  auth?: Partial<AuthContextValue>
}

function AuthStub({ auth, children }: { auth: Partial<AuthContextValue>; children: ReactNode }) {
  const value: AuthContextValue = {
    status: auth.status ?? (auth.user ? 'authenticated' : 'unauthenticated'),
    user: auth.user ?? null,
    login: auth.login ?? vi.fn().mockResolvedValue(undefined),
    logout: auth.logout ?? vi.fn().mockResolvedValue(undefined),
  }
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function renderWithProviders(ui: ReactElement, { route = '/', auth }: RenderOptions = {}) {
  const tree = <MemoryRouter initialEntries={[route]}>{ui}</MemoryRouter>

  return render(<ThemeProvider>{auth ? <AuthStub auth={auth}>{tree}</AuthStub> : tree}</ThemeProvider>)
}

export const authenticatedFixture: Partial<AuthContextValue> = {
  status: 'authenticated',
  user: {
    id: 'user-1',
    email: 'admin@cmms.local',
    isAdmin: false,
    siteMemberships: [{ siteId: 'site-1', siteName: 'Test Site', role: 'Planner' }],
  },
}
