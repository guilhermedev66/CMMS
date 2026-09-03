import { useState, type FormEvent } from 'react'
import { Navigate, useLocation, type Location } from 'react-router-dom'
import { ApiError } from '../api/client'
import { useAuth } from './useAuth'

export function LoginPage() {
  const { status, login } = useAuth()
  const location = useLocation()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  if (status === 'authenticated') {
    const from = (location.state as { from?: Location } | null)?.from
    return <Navigate to={from?.pathname ?? '/'} replace />
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    setSubmitting(true)
    try {
      await login(email, password)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not sign in. Please try again.')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="flex h-screen items-center justify-center bg-surface px-4">
      <div className="w-full max-w-sm rounded-md border border-border bg-surface-raised p-6">
        <div className="mb-6 flex items-center gap-2">
          <div className="flex h-7 w-7 shrink-0 items-center justify-center rounded-sm bg-accent font-mono text-sm font-semibold text-accent-contrast">
            C
          </div>
          <span className="text-sm font-semibold text-text-primary">CMMS</span>
        </div>

        <h1 className="mb-1 text-base font-semibold text-text-primary">Sign in</h1>
        <p className="mb-5 text-sm text-text-secondary">Use your site credentials to continue.</p>

        <form onSubmit={handleSubmit} className="flex flex-col gap-3">
          <label className="flex flex-col gap-1 text-sm">
            <span className="text-text-secondary">Email</span>
            <input
              type="email"
              autoComplete="username"
              required
              value={email}
              onChange={(event) => setEmail(event.target.value)}
              className="rounded-sm border border-border bg-surface px-3 py-1.5 text-sm text-text-primary focus:border-border-strong focus:ring-1 focus:ring-accent focus:outline-none"
            />
          </label>

          <label className="flex flex-col gap-1 text-sm">
            <span className="text-text-secondary">Password</span>
            <input
              type="password"
              autoComplete="current-password"
              required
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              className="rounded-sm border border-border bg-surface px-3 py-1.5 text-sm text-text-primary focus:border-border-strong focus:ring-1 focus:ring-accent focus:outline-none"
            />
          </label>

          {error && (
            <p role="alert" className="rounded-sm border border-status-danger/30 bg-status-danger/10 px-3 py-2 text-sm text-status-danger">
              {error}
            </p>
          )}

          <button
            type="submit"
            disabled={submitting}
            className="mt-2 rounded-sm bg-accent px-3 py-2 text-sm font-medium text-accent-contrast transition-opacity hover:opacity-90 disabled:opacity-60"
          >
            {submitting ? 'Signing in…' : 'Sign in'}
          </button>
        </form>
      </div>
    </div>
  )
}
