import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { appsService } from '../services/apps.service'
import type { App } from '../types/apps.types'
import { useAppEditorStore } from './app-editor.store'

vi.mock('../services/apps.service', () => ({
  appsService: {
    publish: vi.fn(),
  },
}))

describe('app editor publishing', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('preserves the stored remix capability when saving publishing settings', async () => {
    const store = useAppEditorStore()
    store.currentApp = {
      id: 'app-id',
      isForkable: true,
      accessStatus: 'Active',
    } as App

    await store.publishApp({
      publicUrl: 'clock',
      requiresAuthentication: false,
      runtimeVisibility: 'private',
      catalogVisibility: 'listed',
      marketplacePreviewMode: 'mediaOnly',
    })

    expect(appsService.publish).toHaveBeenCalledWith('app-id', expect.objectContaining({
      publicUrl: 'clock',
      isForkable: true,
    }))
    expect(store.currentApp?.isForkable).toBe(true)
  })
})
