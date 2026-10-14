import { afterEach, describe, expect, it, vi } from 'vitest'
import { authService } from './auth.service'

vi.mock('@/shared/services/api/axios-instance', () => ({ default: {}, tokenStorage: {} }))

afterEach(() => { vi.unstubAllEnvs(); vi.unstubAllGlobals() })

describe('GitHub login URL', () => {
  it('uses the configured API when the Docker GitHub override is empty', () => {
    vi.stubEnv('VITE_API_BASE_URL', 'https://api.example.invalid')
    vi.stubEnv('VITE_GITHUB_AUTH_API_BASE_URL', '')
    const assign = vi.fn()
    vi.stubGlobal('window', { location: { origin: 'https://studio.example.invalid', assign } })
    authService.startGitHubLogin('/')
    expect(assign).toHaveBeenCalledWith('https://api.example.invalid/auth/github/login?returnUrl=%2F')
  })
  it('preserves an explicitly configured separate OAuth endpoint', () => {
    vi.stubEnv('VITE_API_BASE_URL', 'https://api.example.invalid')
    vi.stubEnv('VITE_GITHUB_AUTH_API_BASE_URL', 'https://oauth.example.invalid/')
    const assign = vi.fn()
    vi.stubGlobal('window', { location: { origin: 'https://studio.example.invalid', assign } })
    authService.startGitHubLogin('/')
    expect(assign).toHaveBeenCalledWith('https://oauth.example.invalid/auth/github/login?returnUrl=%2F')
  })
})
