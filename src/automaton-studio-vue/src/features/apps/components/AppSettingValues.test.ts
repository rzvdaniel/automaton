// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import ToastService from 'primevue/toastservice'
import AppSettingValues from './AppSettingValues.vue'
import { appsService } from '../services/apps.service'
import type { App } from '../types/apps.types'

vi.mock('../services/apps.service', () => ({
  appsService: { listSettingValues: vi.fn().mockResolvedValue([]), setSettingValue: vi.fn(), deleteSettingValue: vi.fn(), deleteSecret: vi.fn(), restart: vi.fn() },
}))

const declared = [
  { name: 'CHECK_INTERVAL_SECONDS', description: 'How often', default: '30' },
  { name: 'SITE', required: true },
]
const mountSettings = (extra: Partial<App> = {}) => mount(AppSettingValues, {
  props: { appId: 'app-1', app: { id: 'app-1', isPaused: false, ...extra } as App, declared },
  global: { plugins: [PrimeVue, ToastService] },
})

describe('AppSettingValues', () => {
  beforeEach(() => { vi.mocked(appsService.listSettingValues).mockResolvedValue([]) })

  it('offers no restart for an Actions-only app and says changes apply on the next run', async () => {
    vi.mocked(appsService.listSettingValues).mockResolvedValue([{ name: 'SITE', value: 'example.org', updatedAtUtc: '2026-11-01T03:00:00Z' }])
    vi.mocked(appsService.deleteSettingValue).mockResolvedValue()
    const wrapper = mountSettings({ actionsOnly: true })
    await flushPromises()
    await wrapper.find('[aria-label="Use the default for SITE"]').trigger('click')
    await flushPromises()
    expect(wrapper.find('[data-testid="restart-app"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="settings-restart-hint"]').exists()).toBe(false)
    expect(wrapper.get('[data-testid="settings-note"]').text()).toContain('Changes apply on the next run.')
  })

  it('lists every declared setting with its default, even when nothing was saved', async () => {
        const wrapper = mountSettings()
    await flushPromises()
    const text = wrapper.text()
    expect(text).toContain('CHECK_INTERVAL_SECONDS')
    expect(text).toContain('default: 30')
    expect(text).toContain('SITE')
    expect(text).toContain('not set')
    const descriptions = wrapper.findAll('[data-testid="setting-description"]')
    expect(descriptions.map((d) => d.text())).toEqual(['How often'])
  })

  it('shows the current custom value, prefills the replace box, and resets to the default', async () => {
        vi.mocked(appsService.listSettingValues).mockResolvedValue([{ name: 'CHECK_INTERVAL_SECONDS', value: '45', updatedAtUtc: '2026-11-01T03:00:00Z' }])
    vi.mocked(appsService.deleteSettingValue).mockResolvedValue()
    const wrapper = mountSettings()
    await flushPromises()
    expect(wrapper.find('[data-testid="setting-current"]').text()).toBe('45')

    await wrapper.find('[aria-label="Set CHECK_INTERVAL_SECONDS"]').trigger('click')
    expect((wrapper.find('#setting-value').element as HTMLInputElement).value).toBe('45')
    expect(wrapper.find('form.row').text()).toContain('CHECK_INTERVAL_SECONDS')
    await wrapper.find('form.row').trigger('keydown', { key: 'Escape' })
    expect(wrapper.find('form').exists()).toBe(false)

    await wrapper.find('[aria-label="Use the default for CHECK_INTERVAL_SECONDS"]').trigger('click')
    await flushPromises()
    expect(appsService.deleteSettingValue).toHaveBeenCalledWith('app-1', 'CHECK_INTERVAL_SECONDS')
    expect(wrapper.find('[data-testid="settings-restart-hint"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="restart-app"]').exists()).toBe(true)
  })

  it('saves through the settings endpoint', async () => {
        vi.mocked(appsService.listSettingValues).mockResolvedValue([])
    vi.mocked(appsService.setSettingValue).mockResolvedValue()
    const wrapper = mountSettings()
    await flushPromises()
    await wrapper.find('[aria-label="Set SITE"]').trigger('click')
    await wrapper.find('#setting-value').setValue('example.org')
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(appsService.setSettingValue).toHaveBeenCalledWith('app-1', 'SITE', 'example.org')
  })

  it('shows a value that was saved before the settings store, without asking to save it again', async () => {
    vi.mocked(appsService.listSettingValues).mockResolvedValue([{ name: 'CHECK_INTERVAL_SECONDS', value: '45', updatedAtUtc: '2026-11-01T03:00:00Z' }])
    const wrapper = mountSettings()
    await flushPromises()
    expect(wrapper.get('[data-testid="setting-current"]').text()).toBe('45')
    expect(wrapper.text()).not.toContain('save again')
  })

  it('prefills the default when no custom value is set', async () => {
        vi.mocked(appsService.listSettingValues).mockResolvedValue([])
    const wrapper = mountSettings()
    await flushPromises()
    await wrapper.find('[aria-label="Set CHECK_INTERVAL_SECONDS"]').trigger('click')
    expect((wrapper.find('#setting-value').element as HTMLInputElement).value).toBe('30')
  })

  it('restarts the app from the card', async () => {
        vi.mocked(appsService.restart).mockResolvedValue({ restarted: 1, confirmed: true })
    const wrapper = mountSettings()
    await flushPromises()

    await wrapper.find('[data-testid="restart-app"]').trigger('click')
    await flushPromises()

    expect(appsService.restart).toHaveBeenCalledWith('app-1')
  })
})
