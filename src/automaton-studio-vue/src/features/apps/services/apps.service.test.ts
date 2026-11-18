import { beforeEach, describe, expect, it, vi } from 'vitest'
import { appsService } from './apps.service'

const apiClient = vi.hoisted(() => ({
  get: vi.fn(),
  post: vi.fn(),
  delete: vi.fn(),
  put: vi.fn(),
}))

vi.mock('@/shared/services/api/axios-instance', () => ({ default: apiClient }))

describe('AppsService metadata settings', () => {
  beforeEach(() => vi.clearAllMocks())

  it('persists a cleared marketplace category instead of restoring the old value', async () => {
    apiClient.get.mockResolvedValue({ data: {
      id: 'app-id',
      name: 'Clock',
      category: 'Productivity',
      definition: { version: 1 },
      screenshots: [],
    } })

    await appsService.updateSettings('app-id', { category: null })

    expect(apiClient.put).toHaveBeenCalledWith('/api/apps/app-id', expect.objectContaining({ category: null }))
  })

  it('loads marketplace categories through the app service', async () => {
    apiClient.get.mockResolvedValue({ data: ['Productivity', 'Utilities'] })

    await expect(appsService.getMarketplaceCategories()).resolves.toEqual(['Productivity', 'Utilities'])
    expect(apiClient.get).toHaveBeenCalledWith('/api/public/marketplace/categories')
  })
})

describe('AppsService long-running actions', () => {
  beforeEach(() => vi.clearAllMocks())

  it('gives restart and resume a longer timeout than the default', async () => {
    apiClient.post.mockResolvedValue({ data: {} })
    await appsService.restart('a')
    await appsService.setPaused('a', false)
    expect(apiClient.post).toHaveBeenNthCalledWith(1, '/api/apps/a/restart', undefined, { timeout: 180000 })
    expect(apiClient.post).toHaveBeenNthCalledWith(2, '/api/apps/a/resume', undefined, { timeout: 180000 })
  })

  it('sends the pause reason as the request body, and nothing on resume', async () => {
    apiClient.post.mockResolvedValue({ data: {} })
    await appsService.setPaused('a', true, '  Sends spam ')
    await appsService.setPaused('a', true)
    await appsService.setPaused('a', false, 'ignored')
    expect(apiClient.post).toHaveBeenNthCalledWith(1, '/api/apps/a/pause', { reason: 'Sends spam' }, { timeout: 180000 })
    expect(apiClient.post).toHaveBeenNthCalledWith(2, '/api/apps/a/pause', undefined, { timeout: 180000 })
    expect(apiClient.post).toHaveBeenNthCalledWith(3, '/api/apps/a/resume', undefined, { timeout: 180000 })
  })

  it('reads the app after the browser drops a resume, and reports success once the server shows it', async () => {
    vi.useFakeTimers()
    apiClient.post.mockRejectedValue({ code: 'ERR_NETWORK' })
    apiClient.get.mockResolvedValueOnce({ data: { isPaused: true } }).mockResolvedValueOnce({ data: { isPaused: false } })
    const result = appsService.setPaused('a', false)
    await vi.advanceTimersByTimeAsync(6000)
    await expect(result).resolves.toMatchObject({ appId: 'a', paused: false, warnings: [] })
    vi.useRealTimers()
  })

  it('still fails when the server answered with an error, or the dropped call never took effect', async () => {
    apiClient.post.mockRejectedValueOnce({ code: 'ERR_BAD_RESPONSE', response: { status: 502 } })
    await expect(appsService.setPaused('a', true)).rejects.toMatchObject({ response: { status: 502 } })

    vi.useFakeTimers()
    apiClient.post.mockRejectedValue({ code: 'ERR_NETWORK' })
    apiClient.get.mockResolvedValue({ data: { isPaused: false } })
    const result = appsService.setPaused('a', true)
    const settled = result.catch((failure) => failure)
    await vi.advanceTimersByTimeAsync(160_000)
    expect(await settled).toMatchObject({ code: 'ERR_NETWORK' })
    vi.useRealTimers()
  })

  it('tells a restart whose answer was lost apart from a confirmed one', async () => {
    apiClient.post.mockResolvedValueOnce({ data: { restarted: 2 } }).mockRejectedValueOnce({ code: 'ERR_NETWORK' })
    await expect(appsService.restart('a')).resolves.toEqual({ restarted: 2, confirmed: true })
    await expect(appsService.restart('a')).resolves.toEqual({ restarted: 1, confirmed: false })
  })
})
