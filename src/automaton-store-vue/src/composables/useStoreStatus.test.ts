import { afterEach, describe, expect, it, vi } from 'vitest'
import { fetchStoreEnabled } from './useStoreStatus'

function mockFetch(status: number, body: unknown) {
  vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify(body), { status })))
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('fetchStoreEnabled', () => {
  it('reports a disabled store', async () => {
    mockFetch(200, { enabled: false })
    await expect(fetchStoreEnabled('https://api.test/')).resolves.toBe(false)
  })

  it('keeps the store available when status is enabled or unknown', async () => {
    mockFetch(200, { enabled: true })
    await expect(fetchStoreEnabled('https://api.test/')).resolves.toBe(true)

    mockFetch(404, {})
    await expect(fetchStoreEnabled('https://api.test/')).resolves.toBe(true)

    vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('offline') }))
    await expect(fetchStoreEnabled('https://api.test/')).resolves.toBe(true)
  })
})
