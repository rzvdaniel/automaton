import { afterEach, describe, expect, it, vi } from 'vitest'
import { getDeployedAppUrl, getDeploymentTargetUrl } from './public-app-url'

afterEach(() => {
  vi.unstubAllEnvs()
  vi.unstubAllGlobals()
})

describe('deployment target URLs', () => {
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
