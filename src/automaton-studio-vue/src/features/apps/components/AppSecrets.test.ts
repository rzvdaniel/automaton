// @vitest-environment jsdom
import { describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import ConfirmationService from 'primevue/confirmationservice'
import AppSecrets from './AppSecrets.vue'
import { appsService } from '../services/apps.service'

vi.mock('../services/apps.service', () => ({
  appsService: { listSecrets: vi.fn(), setSecret: vi.fn(), deleteSecret: vi.fn() },
}))

const mountSecrets = () => mount(AppSecrets, {
  props: { appId: 'app-1' },
  global: { plugins: [PrimeVue, ConfirmationService] },
})

const saveButton = (wrapper: ReturnType<typeof mountSecrets>) =>
  wrapper.findAll('button').find((button) => button.text().includes('Save secret'))!

describe('AppSecrets', () => {
  it('lists secret names without values and saves a new value', async () => {
    vi.mocked(appsService.listSecrets).mockResolvedValue([
      { name: 'S3_BUCKET', version: 1, updatedAtUtc: '2026-11-01T03:00:00Z' },
    ])
    vi.mocked(appsService.setSecret).mockResolvedValue()
    const wrapper = mountSecrets()
    await flushPromises()
    expect(wrapper.text()).toContain('S3_BUCKET')

    await wrapper.find('#secret-name').setValue('S3_SECRET_KEY')
    await wrapper.find('#secret-value').setValue('very-secret')
    await saveButton(wrapper).trigger('click')
    await flushPromises()

    expect(appsService.setSecret).toHaveBeenCalledWith('app-1', 'S3_SECRET_KEY', 'very-secret')
    expect(wrapper.text()).not.toContain('very-secret')
  })

  it('rejects names that are not environment variable names', async () => {
    vi.mocked(appsService.listSecrets).mockResolvedValue([])
    vi.mocked(appsService.setSecret).mockClear()
    const wrapper = mountSecrets()
    await flushPromises()

    await wrapper.find('#secret-name').setValue('AUTOMATON_APP_ID')
    await wrapper.find('#secret-value').setValue('x')
    await saveButton(wrapper).trigger('click')
    await flushPromises()

    expect(appsService.setSecret).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('not starting with a digit or AUTOMATON_')
  })
})
