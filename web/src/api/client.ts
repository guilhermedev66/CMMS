/**
 * Thin fetch wrapper for the real backend (see src/Cmms.Api/AuthEndpoints.cs,
 * AssetsEndpoints.cs). Handles the two things every call needs:
 *  - cookie-session auth: `credentials: 'include'` on every request
 *  - CSRF: GET /auth/csrf returns a request token (paired with an
 *    HttpOnly antiforgery cookie already set by that call); every
 *    mutation sends it back as X-CSRF-TOKEN. Fetched once and cached —
 *    the antiforgery cookie is independent of the auth session, so it
 *    survives login/logout — with a refetch-and-retry-once fallback if
 *    the server ever rejects it as stale/missing.
 *
 * Requests go through Vite's dev proxy at /api -> localhost:8080 (see
 * vite.config.ts) so the browser sees everything as same-origin.
 */

const API_BASE = '/api'

export class ApiError extends Error {
  status: number
  fieldErrors?: Record<string, string[]>

  constructor(status: number, message: string, fieldErrors?: Record<string, string[]>) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.fieldErrors = fieldErrors
  }
}

let csrfToken: string | null = null
let csrfTokenPromise: Promise<string> | null = null

async function fetchCsrfToken(): Promise<string> {
  const res = await fetch(`${API_BASE}/auth/csrf`, { credentials: 'include' })
  if (!res.ok) throw new ApiError(res.status, 'Could not obtain a CSRF token.')
  const data = (await res.json()) as { token: string }
  return data.token
}

async function getCsrfToken(forceRefresh = false): Promise<string> {
  if (csrfToken && !forceRefresh) return csrfToken
  if (!csrfTokenPromise || forceRefresh) {
    csrfTokenPromise = fetchCsrfToken()
  }
  csrfToken = await csrfTokenPromise
  return csrfToken
}

type UnauthorizedListener = () => void
const unauthorizedListeners = new Set<UnauthorizedListener>()

/** AuthProvider subscribes so a 401 from *any* call (not just /auth/me) drops the session client-side. */
export function onUnauthorized(listener: UnauthorizedListener): () => void {
  unauthorizedListeners.add(listener)
  return () => unauthorizedListeners.delete(listener)
}

interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT'
  body?: unknown
  /** GET /auth/me uses this: a 401 there just means "not logged in yet", not a dropped session. */
  suppressUnauthorizedEvent?: boolean
}

async function doFetch(path: string, method: string, body: unknown, csrf?: string): Promise<Response> {
  const headers: Record<string, string> = {}
  if (body !== undefined) headers['Content-Type'] = 'application/json'
  if (csrf) headers['X-CSRF-TOKEN'] = csrf

  return fetch(`${API_BASE}${path}`, {
    method,
    credentials: 'include',
    headers,
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })
}

async function parseErrorMessage(res: Response): Promise<{ message: string; fieldErrors?: Record<string, string[]> }> {
  let payload: unknown
  try {
    payload = await res.json()
  } catch {
    return { message: `Request failed (${res.status})` }
  }

  if (payload && typeof payload === 'object') {
    const problem = payload as { title?: string; error?: string; errors?: Record<string, string[]> }
    if (problem.errors) {
      const message = Object.values(problem.errors).flat().join(' ') || problem.title || 'Validation failed.'
      return { message, fieldErrors: problem.errors }
    }
    if (problem.title) return { message: problem.title }
    if (problem.error) return { message: problem.error }
  }

  return { message: `Request failed (${res.status})` }
}

async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const method = options.method ?? 'GET'
  const isMutation = method !== 'GET'

  let res = isMutation
    ? await doFetch(path, method, options.body, await getCsrfToken())
    : await doFetch(path, method, options.body)

  if (isMutation && res.status === 400) {
    const probe = (await res
      .clone()
      .json()
      .catch(() => null)) as { error?: string } | null
    if (probe?.error === 'Invalid anti-forgery token.') {
      res = await doFetch(path, method, options.body, await getCsrfToken(true))
    }
  }

  if (res.status === 401) {
    if (!options.suppressUnauthorizedEvent) {
      unauthorizedListeners.forEach((listener) => listener())
    }
    throw new ApiError(401, 'Your session has expired. Please log in again.')
  }

  if (!res.ok) {
    const { message, fieldErrors } = await parseErrorMessage(res)
    throw new ApiError(res.status, message, fieldErrors)
  }

  if (res.status === 204) return undefined as T
  return (await res.json()) as T
}

export const apiClient = {
  get: <T>(path: string, options?: Pick<RequestOptions, 'suppressUnauthorizedEvent'>) =>
    request<T>(path, { ...options, method: 'GET' }),
  post: <T>(path: string, body?: unknown) => request<T>(path, { method: 'POST', body }),
  put: <T>(path: string, body?: unknown) => request<T>(path, { method: 'PUT', body }),
}
