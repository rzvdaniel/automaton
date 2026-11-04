// @vitest-environment jsdom
import { mount, flushPromises } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import InstallView from './InstallView.vue'
import { installService } from '../services/install.service'
vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn(), replace: vi.fn() }) }))
vi.mock('../services/install.service', () => ({ installService: { getStatus: vi.fn() } }))
const options = { global: { stubs: {
  Card: { template: '<section><slot name="title" /><slot name="subtitle" /><slot name="content" /></section>' },
  Button: { props: ['label'], template: '<button>{{ label }}</button>' },
  Message: { template: '<div><slot /></div>' },
  Password: { props: ['modelValue'], template: '<input type="password" :value="modelValue" />' },
  InputText: { props: ['modelValue'], template: '<input :value="modelValue" />' },
  SetupChecks: { name: 'SetupChecks', props: ['setupToken'], template: '<div />' },
} } }
describe('first administrator setup', () => {
  afterEach(() => window.history.replaceState(null, '', '/'))
  it('accepts the setup link and removes its token from the address bar', async () => {
    vi.mocked(installService.getStatus).mockResolvedValue({ installationComplete: false, setupTokenRequired: true, gitHubEnabled: false, userSignUp: false })
    window.history.replaceState(null, '', '/install?source=installer#token=link-token')
    const wrapper = mount(InstallView, options)
    await flushPromises()
    expect(window.location.search).toBe('?source=installer')
    expect(window.location.hash).toBe('')
    expect((wrapper.find('input[type="password"]').element as HTMLInputElement).value).toBe('link-token')
    expect(wrapper.text()).not.toContain('link-token')
  })
  it('shows connection recovery instead of a create-admin form when status is unavailable', async () => {
    vi.mocked(installService.getStatus).mockRejectedValue(new Error('offline'))
    const wrapper = mount(InstallView, options)
    await flushPromises()
    expect(wrapper.text()).toContain('Retry connection')
    expect(wrapper.find('form').exists()).toBe(false)
  })
  it('keeps setup closed for an existing installation', async () => {
    vi.mocked(installService.getStatus).mockResolvedValue({ installationComplete: true, setupTokenRequired: true, gitHubEnabled: false, userSignUp: false })
    const wrapper = mount(InstallView, options)
    await flushPromises()
    expect(wrapper.text()).toContain('already configured')
    expect(wrapper.find('form').exists()).toBe(false)
  })
})
