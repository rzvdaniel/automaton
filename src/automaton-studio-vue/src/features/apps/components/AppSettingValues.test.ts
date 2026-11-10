// @vitest-environment jsdom
import { describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import ToastService from 'primevue/toastservice'
import AppSettingValues from './AppSettingValues.vue'
import { appsService } from '../services/apps.service'
import type { App } from '../types/apps.types'

vi.mock('../services/apps.service', () => ({
  appsService: { listSecrets: vi.fn(), setSecret: vi.fn(), deleteSecret: vi.fn(), restart: vi.fn() },
}))

const declared = [
  { name: 'CHECK_INTERVAL_SECONDS', description: 'How often', default: '30' },
  { name: 'SITE', required: true },
]
const mountSettings = () => mount(AppSettingValues, {
  props: { appId: 'app-1', app: { id: 'app-1', isPaused: false } as App, declared },
  global: { plugins: [PrimeVue, ToastService] },
})

describe('AppSettingValues', () => {
  it('lists every declared setting with its default, even when nothing was saved', async () => {
    vi.mocked(appsService.listSecrets).mockResolvedValue([])
    const wrapper = mountSettings()
    await flushPromises()
    const text = wrapper.text()
    expect(text).toContain('CHECK_INTERVAL_SECONDS')
    expect(text).toContain('default: 30')
    expect(text).toContain('SITE')
    expect(text).toContain('not set')
  })

  it('shows a saved value as custom, resets it to the default, and offers a restart', async () => {
    vi.mocked(appsService.listSecrets).mockResolvedValue([{ name: 'CHECK_INTERVAL_SECONDS', version: 1, updatedAtUtc: '2026-11-01T03:00:00Z' }])
    vi.mocked(appsService.deleteSecret).mockResolvedValue()
    const wrapper = mountSettings()
    await flushPromises()
    expect(wrapper.text()).toContain('custom value set')

    await wrapper.find('[aria-label="Use the default for CHECK_INTERVAL_SECONDS"]').trigger('click')
    await flushPromises()

    expect(appsService.deleteSecret).toHaveBeenCalledWith('app-1', 'CHECK_INTERVAL_SECONDS')
    expect(wrapper.find('[data-testid="settings-restart-hint"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="restart-app"]').exists()).toBe(true)
  })

  it('restarts the app from the card', async () => {
    vi.mocked(appsService.listSecrets).mockResolvedValue([])
    vi.mocked(appsService.restart).mockResolvedValue({ restarted: 1 })
    const wrapper = mountSettings()
    await flushPromises()

    await wrapper.find('[data-testid="restart-app"]').trigger('click')
    await flushPromises()

    expect(appsService.restart).toHaveBeenCalledWith('app-1')
  })
})
