// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import ToastService from 'primevue/toastservice'
import AppActionWebhooks from './AppActionWebhooks.vue'
import { actionWebhooksService, type ActionWebhook } from '../services/action-webhooks.service'

vi.mock('../services/action-webhooks.service', () => ({
  actionWebhooksService: { create: vi.fn(), rotate: vi.fn(), setEnabled: vi.fn(), remove: vi.fn() },
}))

const hook: ActionWebhook = {
  id: 'w1', applicationId: 'a', actionKey: 'docs-walk', name: 'Docs walk', enabled: true, createdAtUtc: new Date().toISOString(),
  url: 'https://api.example.test/api/hooks/w1', calls: [{ calledAtUtc: new Date().toISOString(), outcome: 'Started' }],
}

function mountIt(webhooks: ActionWebhook[] = [hook], canUpdate = true, adding = false) {
  return mount(AppActionWebhooks, {
    props: { appId: 'a', actionKey: 'docs-walk', actionName: 'Docs walk', webhooks, canUpdate, adding },
    global: { plugins: [PrimeVue, ToastService] },
  })
}

describe('AppActionWebhooks', () => {
  beforeEach(() => vi.clearAllMocks())

  it('lists the webhook with its address and last call', () => {
    const text = mountIt().text()
    expect(text).toContain('Docs walk')
    expect(text).toContain('https://api.example.test/api/hooks/w1')
    expect(text).toContain('last call started')
  })

  it('adds a webhook and shows its secret once', async () => {
    vi.mocked(actionWebhooksService.create).mockResolvedValue({ ...hook, secret: 'whk_secret' })
    const wrapper = mountIt()
    await wrapper.setProps({ adding: true })
    await wrapper.get('[data-testid="webhook-name-input"]').setValue('Docs walk')
    await wrapper.get('[data-testid="webhook-create"]').trigger('click')
    await flushPromises()
    expect(actionWebhooksService.create).toHaveBeenCalledWith('a', 'docs-walk', 'Docs walk')
    expect(wrapper.get('[data-testid="webhook-secret"]').text()).toContain('whk_secret')
    expect(wrapper.emitted('changed')).toBeTruthy()
  })

  it('rotates the secret and removes the webhook', async () => {
    vi.mocked(actionWebhooksService.rotate).mockResolvedValue({ ...hook, secret: 'whk_new' })
    const wrapper = mountIt()
    await wrapper.get('[data-testid="webhook-edit"]').trigger('click')
    await wrapper.get('[data-testid="webhook-rotate"]').trigger('click')
    await wrapper.get('[data-testid="webhook-rotate-confirm"]').trigger('click')
    await flushPromises()
    expect(wrapper.get('[data-testid="webhook-secret"]').text()).toContain('whk_new')
    await wrapper.get('[data-testid="webhook-remove"]').trigger('click')
    expect(actionWebhooksService.remove).not.toHaveBeenCalled()
    await wrapper.get('[data-testid="webhook-remove-confirm"]').trigger('click')
    await flushPromises()
    expect(actionWebhooksService.remove).toHaveBeenCalledWith('a', 'w1')
  })

  it('turns the webhook off', async () => {
    vi.mocked(actionWebhooksService.setEnabled).mockResolvedValue(hook)
    const wrapper = mountIt()
    await wrapper.get('[data-testid="webhook-switch"] input').setValue(false)
    await flushPromises()
    expect(actionWebhooksService.setEnabled).toHaveBeenCalledWith('a', 'w1', false)
  })

  it('offers no changes to people who cannot update the app', () => {
    const wrapper = mountIt([hook], false, true)
    expect(wrapper.find('[data-testid="webhook-add-form"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="webhook-edit"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="webhook-switch"]').exists()).toBe(false)
  })
})
