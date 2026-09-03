import { apiClient } from './client'

export interface AuthUser {
  id: string
  email: string
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
