import { afterEach, describe, expect, it, vi } from 'vitest'
import { fetchList, useMarketplaceApps } from './useMarketplaceApps'

function mockFetch(status: number, body: unknown) {
  vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify(body), { status })))
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('fetchList', () => {
  it('returns list responses', async () => {
    mockFetch(200, ['a', 'b'])
    await expect(fetchList<string>('https://api.test/x')).resolves.toEqual(['a', 'b'])
  })

  it('rejects error statuses and non-list bodies', async () => {
    mockFetch(500, [])
    await expect(fetchList('https://api.test/x')).rejects.toThrow('500')

    mockFetch(200, { title: 'Problem' })
    await expect(fetchList('https://api.test/x')).rejects.toThrow('list')
  })
})

describe('useMarketplaceApps', () => {
  it('keeps empty lists and reports an error when the API fails', async () => {
    mockFetch(503, { title: 'Unavailable' })
    const store = useMarketplaceApps()

    await store.loadCategories()
    await store.loadApps()

    expect(store.categories.value).toEqual([])
    expect(store.apps.value).toEqual([])
    expect(store.error.value).toBeInstanceOf(Error)
    expect(store.loading.value).toBe(false)
  })
})
