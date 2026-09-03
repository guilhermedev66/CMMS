import { createContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import * as authApi from '../api/auth'
import { onUnauthorized } from '../api/client'

export type AuthStatus = 'loading' | 'authenticated' | 'unauthenticated'

export interface AuthContextValue {
  status: AuthStatus
  user: authApi.AuthUser | null
  login: (email: string, password: string) => Promise<void>
  logout: () => Promise<void>
}

export const AuthContext = createContext<AuthContextValue | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<AuthStatus>('loading')
  const [user, setUser] = useState<authApi.AuthUser | null>(null)

  useEffect(() => {
    let cancelled = false
    authApi
      .me()
      .then((current) => {
        if (cancelled) return
        setUser(current)
        setStatus('authenticated')
      })
      .catch(() => {
        if (cancelled) return
        setUser(null)
        setStatus('unauthenticated')
      })
    return () => {
      cancelled = true
    }
  }, [])

  useEffect(() => {
    // A 401 on *any* call (session expired mid-use, not just /auth/me) drops
    // the session client-side so ProtectedRoute sends the user back to login
    // instead of leaving every page stuck on a generic error banner.
    return onUnauthorized(() => {
      setUser(null)
      setStatus('unauthenticated')
    })
  }, [])

  const value = useMemo<AuthContextValue>(
    () => ({
      status,
      user,
      async login(email, password) {
        await authApi.login(email, password)
        const current = await authApi.me()
        setUser(current)
        setStatus('authenticated')
      },
      async logout() {
        await authApi.logout()
        setUser(null)
        setStatus('unauthenticated')
      },
    }),
    [status, user],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
