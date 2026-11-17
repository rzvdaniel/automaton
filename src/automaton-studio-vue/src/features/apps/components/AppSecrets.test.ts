// @vitest-environment jsdom
import { describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import ConfirmationService from 'primevue/confirmationservice'
import AppSecrets from './AppSecrets.vue'
import { appsService } from '../services/apps.service'

vi.mock('../services/apps.service', () => ({
  appsService: { listSecrets: vi.fn(), setSecret: vi.fn(), deleteSecret: vi.fn(), listSecretHistory: vi.fn() },
}))

const mountSecrets = () => mount(AppSecrets, {
  props: { appId: 'app-1', declared: [{ name: 'S3_BUCKET' }] },
  global: { plugins: [PrimeVue, ConfirmationService] },
})

const openForm = (wrapper: ReturnType<typeof mountSecrets>) => wrapper.find('[data-testid="add-secret"]').trigger('click')

describe('AppSecrets', () => {
  it('lists secret names without values and saves a new value', async () => {
    vi.mocked(appsService.listSecrets).mockResolvedValue([
      { name: 'S3_BUCKET', version: 1, updatedAtUtc: '2026-11-01T03:00:00Z' },
    ])
    vi.mocked(appsService.setSecret).mockResolvedValue()
    const wrapper = mountSecrets()
    await flushPromises()
    expect(wrapper.text()).toContain('S3_BUCKET')
    await openForm(wrapper)

    await wrapper.find('#secret-name').setValue('S3_SECRET_KEY')
    await wrapper.find('#secret-value').setValue('very-secret')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(appsService.setSecret).toHaveBeenCalledWith('app-1', 'S3_SECRET_KEY', 'very-secret')
    expect(wrapper.text()).not.toContain('very-secret')
  })

  it('keeps settings off the secrets list', async () => {
    vi.mocked(appsService.listSecrets).mockResolvedValue([
      { name: 'S3_BUCKET', version: 1, updatedAtUtc: '2026-11-01T03:00:00Z' },
      { name: 'CHECK_INTERVAL_SECONDS', version: 1, updatedAtUtc: '2026-11-01T03:00:00Z' },
    ])
    const wrapper = mount(AppSecrets, {
      props: { appId: 'app-1', declared: [{ name: 'S3_BUCKET' }], settingNames: ['CHECK_INTERVAL_SECONDS'] },
      global: { plugins: [PrimeVue, ConfirmationService] },
    })
    await flushPromises()
    expect(wrapper.text()).toContain('S3_BUCKET')
    expect(wrapper.text()).not.toContain('CHECK_INTERVAL_SECONDS')
  })

  it('rejects names that are not environment variable names', async () => {
    vi.mocked(appsService.listSecrets).mockResolvedValue([])
    vi.mocked(appsService.setSecret).mockClear()
    const wrapper = mountSecrets()
    await flushPromises()
    await openForm(wrapper)

    await wrapper.find('#secret-name').setValue('AUTOMATON_APP_ID')
    await wrapper.find('#secret-value').setValue('x')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(appsService.setSecret).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('not starting with a digit or AUTOMATON_')
  })
  it('marks the name and value inputs so password managers do not fill in saved logins (#894)', async () => {
    vi.mocked(appsService.listSecrets).mockResolvedValue([])
    const wrapper = mountSecrets()
    await flushPromises()
    await openForm(wrapper)

    const nameInput = wrapper.find('input#secret-name').element as HTMLInputElement
    const valueInput = wrapper.find('input#secret-value').element as HTMLInputElement
    expect(nameInput.getAttribute('autocomplete')).toBe('off')
    expect(valueInput.getAttribute('autocomplete')).toBe('new-password')
    expect(valueInput.type).toBe('password')
    for (const input of [nameInput, valueInput]) {
      expect(input.getAttribute('name')).toMatch(/^secret-/)
      expect(input.getAttribute('data-1p-ignore')).not.toBeNull()
      expect(input.getAttribute('data-lpignore')).toBe('true')
    }
  })

  it('replaces a value in its own row, focus in the input, Esc cancels', async () => {
    vi.mocked(appsService.listSecrets).mockResolvedValue([
      { name: 'S3_BUCKET', version: 1, updatedAtUtc: '2026-11-01T03:00:00Z' },
    ])
    vi.mocked(appsService.setSecret).mockClear()
    const wrapper = mountSecrets()
    await flushPromises()

    await wrapper.findAll('button').find((button) => button.text() === 'replace')!.trigger('click')
    expect(wrapper.find('input#secret-name').exists()).toBe(false)
    const row = wrapper.find('form.row')
    expect(row.text()).toContain('S3_BUCKET')
    expect(row.find('input#secret-value').exists()).toBe(true)
    await row.trigger('keydown', { key: 'Escape' })
    expect(wrapper.find('form').exists()).toBe(false)
  })

  it('typing an existing name into Add a secret keeps the Add form', async () => {
    vi.mocked(appsService.listSecrets).mockResolvedValue([
      { name: 'S3', version: 1, updatedAtUtc: '2026-11-01T03:00:00Z' },
    ])
    const wrapper = mountSecrets()
    await flushPromises()
    await wrapper.find('[data-testid="add-secret"]').trigger('click')
    await wrapper.find('input#secret-name').setValue('S3')
    expect(wrapper.find('input#secret-name').exists()).toBe(true)
    expect(wrapper.find('form.row').exists()).toBe(false)
  })

  it('always shows the card; Add a secret opens the form in place, focuses Name, and Cancel closes it', async () => {
    vi.mocked(appsService.listSecrets).mockResolvedValue([])
    const wrapper = mount(AppSecrets, {
      props: { appId: 'app-1' },
      attachTo: document.body,
      global: { plugins: [PrimeVue, ConfirmationService] },
    })
    await flushPromises()
    expect(wrapper.find('[data-testid="secrets-card"]').text()).toContain('No secrets yet')
    expect(wrapper.find('input#secret-name').exists()).toBe(false)
    await openForm(wrapper)
    await flushPromises()
    expect(wrapper.find('input#secret-name').element).toBe(document.activeElement)
    expect(wrapper.find('[data-testid="secrets-card"]').text()).toContain('No secrets yet')
    await wrapper.findAll('button').find((button) => button.text() === 'Cancel')!.trigger('click')
    expect(wrapper.find('input#secret-name').exists()).toBe(false)
    wrapper.unmount()
  })

  it('lists the secrets the app needs and are not set yet', async () => {
    vi.mocked(appsService.listSecrets).mockResolvedValue([])
    const wrapper = mount(AppSecrets, {
      props: { appId: 'app-1', declared: [{ name: 'API_KEY', description: 'For the vendor' }] },
      global: { plugins: [PrimeVue, ConfirmationService] },
    })
    await flushPromises()
    expect(wrapper.find('[data-testid="missing-secrets"]').text()).toContain('API_KEY')
    expect(wrapper.get('[data-testid="secret-description"]').text()).toBe('For the vendor')
  })

  it('shows the description under the name of a secret that is set', async () => {
    vi.mocked(appsService.listSecrets).mockResolvedValue([{ name: 'API_KEY', version: 1, updatedAtUtc: '2026-11-01T03:00:00Z' }])
    const wrapper = mount(AppSecrets, {
      props: { appId: 'app-1', declared: [{ name: 'API_KEY', description: 'For the vendor' }] },
      global: { plugins: [PrimeVue, ConfirmationService] },
    })
    await flushPromises()
    expect(wrapper.get('[data-testid="secret-description"]').text()).toBe('For the vendor')
  })
})
