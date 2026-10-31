import { describe, expect, it } from 'vitest'
import { buildPublishedAppUrl, resolveStoreConfig } from './resolveStoreConfig'

describe('resolveStoreConfig', () => {
  it('uses installer runtime values first', () => {
    const config = resolveStoreConfig({
      runtime: {
        apiBaseUrl: 'https://api.example.com',
        publicAppsBaseUrl: 'https://apps.example.com/',
        studioUrl: 'https://studio.example.com',
        storeTitle: 'Example Apps',
      },
      env: { VITE_API_BASE_URL: 'https://ignored.test' },
      origin: 'https://store.example.com',
    })

    expect(config).toEqual({
      apiBaseUrl: 'https://api.example.com/',
      publicAppsBaseUrl: 'https://apps.example.com',
      studioUrl: 'https://studio.example.com/',
      storeTitle: 'Example Apps',
      logoUrl: '',
    })
  })

  it('derives sibling hosts from store.<domain> when runtime config is empty', () => {
    const config = resolveStoreConfig({ runtime: { apiBaseUrl: '' }, origin: 'https://store.example.com' })

    expect(config.apiBaseUrl).toBe('https://api.example.com/')
    expect(config.publicAppsBaseUrl).toBe('https://apps.example.com')
    expect(config.studioUrl).toBe('https://studio.example.com/')
    expect(config.storeTitle).toBe('App Store')
  })

  it('falls back to build environment for local development', () => {
    const config = resolveStoreConfig({
      env: { VITE_API_BASE_URL: 'https://localhost:7091' },
      origin: 'http://localhost:5176',
    })

    expect(config.apiBaseUrl).toBe('https://localhost:7091/')
    expect(config.publicAppsBaseUrl).toBe('https://localhost:7091')
    expect(config.studioUrl).toBe('')
  })

  it('serves published apps from the API when no apps host is known', () => {
    const config = resolveStoreConfig({ origin: 'https://apps-catalog.example.org' })

    expect(config.apiBaseUrl).toBe('https://apps-catalog.example.org/')
    expect(config.publicAppsBaseUrl).toBe('https://apps-catalog.example.org')
  })
})

describe('buildPublishedAppUrl', () => {
  it('uses per-app subdomains under apps.<domain>', () => {
    expect(buildPublishedAppUrl('https://apps.example.com', 'my-app', 'https://store.example.com')).toBe(
      'https://my-app.apps.example.com/',
    )
  })

  it('namespaces paths for local hosts', () => {
    expect(buildPublishedAppUrl('https://localhost:7091', 'my-app', 'http://localhost:5176')).toBe(
      'https://localhost:7091/apps/my-app/',
    )
  })

  it('namespaces paths when the base has a path or is a loopback address', () => {
    expect(buildPublishedAppUrl('https://example.com/prefix', 'my-app', 'https://store.example.com')).toBe(
      'https://example.com/prefix/apps/my-app/',
    )
    expect(buildPublishedAppUrl('http://127.0.0.1:7091', 'a b', 'http://localhost:5176')).toBe(
      'http://127.0.0.1:7091/apps/a%20b/',
    )
  })

  it('returns nothing without a slug or base', () => {
    expect(buildPublishedAppUrl('https://apps.example.com', ' ', 'https://store.example.com')).toBe('')
    expect(buildPublishedAppUrl('', 'my-app', 'https://store.example.com')).toBe('')
  })
})
