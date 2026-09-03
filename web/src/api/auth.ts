import { apiClient } from './client'

export type RoleCode = 'Admin' | 'Planner' | 'Technician' | 'Requester'

export interface SiteMembership {
  siteId: string
  siteName: string
  role: RoleCode
}

/** Matches the /auth/me response shape in src/Cmms.Api/AuthEndpoints.cs. */
export interface AuthUser {
  id: string
  email: string
  isAdmin: boolean
  siteMemberships: SiteMembership[]
}

export function login(email: string, password: string): Promise<void> {
  return apiClient.post<void>('/auth/login', { email, password })
}

export function logout(): Promise<void> {
  return apiClient.post<void>('/auth/logout')
}

/** 401 here just means "no session yet" — callers should not treat it as an unexpected error. */
export function me(): Promise<AuthUser> {
  return apiClient.get<AuthUser>('/auth/me', { suppressUnauthorizedEvent: true })
}
