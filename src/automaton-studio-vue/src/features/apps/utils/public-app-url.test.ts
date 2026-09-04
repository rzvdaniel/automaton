import { afterEach, describe, expect, it, vi } from 'vitest'
import { getDeployedAppUrl, getDeploymentTargetUrl, getPublishedAppUrl } from './public-app-url'

afterEach(() => {
  vi.unstubAllEnvs()
  vi.unstubAllGlobals()
})

describe('deployment target URLs', () => {
  it('keeps website and Studio private links on the same configured host', async () => {
    vi.stubEnv('VITE_PUBLIC_APPS_BASE_URL', 'https://apps.example.test:8443/ignored?unused=1#fragment')
    vi.stubGlobal('window', { location: { origin: 'https://studio.example.test' } })
    const website = await import('../../../../../automaton-vue/src/config/publicAppConfig')
    const appId = '71db73ba-6f48-4fc9-abd0-52a4a44564c8'
    expect(website.getDeployedAppUrl(appId)).toBe(getDeploymentTargetUrl(appId, '/'))
    expect(website.getDeployedAppUrl(null)).toBe('')
  })

  it('uses the private host root for the primary API route without carrying query or fragment', () => {
    vi.stubEnv('VITE_PUBLIC_APPS_BASE_URL', 'https://apps.example.test:8443/ignored?token=unused#fragment')
    vi.stubGlobal('window', { location: { origin: 'https://studio.example.test' } })
    expect(getDeploymentTargetUrl('71db73ba-6f48-4fc9-abd0-52a4a44564c8', '/'))
      .toBe('https://app-71db73ba6f484fc9abd052a4a44564c8.apps.example.test:8443/')
  })

  it('uses the canonical production subdomain for published apps', () => {
    vi.stubEnv('VITE_PUBLIC_APPS_BASE_URL', 'https://apps.automaton.monster')
    vi.stubGlobal('window', { location: { origin: 'https://studio.automaton.monster' } })

    expect(getPublishedAppUrl('space-invaders')).toBe('https://space-invaders.apps.automaton.monster/')
  })

  it('uses the app-specific private host for a named target route', () => {
    vi.stubEnv('VITE_PUBLIC_APPS_BASE_URL', 'https://apps.automaton.example')
    vi.stubGlobal('window', { location: { origin: 'https://studio.automaton.example' } })

    const appId = '71db73ba-6f48-4fc9-abd0-52a4a44564c8'
    const targetId = 'a04a376d-a70d-4905-9524-d0b15db8ad2d'
    const deployedAppUrl = new URL(getDeployedAppUrl(appId))
    const targetUrl = new URL(getDeploymentTargetUrl(appId, `/a/${appId}/t/${targetId}/`))

    expect(targetUrl.origin).toBe(deployedAppUrl.origin)
    expect(targetUrl.hostname).toBe('app-71db73ba6f484fc9abd052a4a44564c8.apps.automaton.example')
    expect(targetUrl.pathname).toBe(`/a/${appId}/t/${targetId}/`)
  })
})
