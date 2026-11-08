import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { appsService } from '../services/apps.service'
import type { App } from '../types/apps.types'
import { useAppEditorStore } from './app-editor.store'

vi.mock('../services/apps.service', () => ({
  appsService: {
    get: vi.fn(),
  },
}))

describe('app editor store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('refreshes the app without the page-wide loading state so the Share dialog stays open', async () => {
    const store = useAppEditorStore()
    vi.mocked(appsService.get).mockResolvedValue({ id: 'app-id', isPublished: true } as App)

    const refreshing = store.refreshApp('app-id')
    expect(store.isLoading).toBe(false)
    await refreshing

    expect(store.currentApp?.isPublished).toBe(true)
  })
})
