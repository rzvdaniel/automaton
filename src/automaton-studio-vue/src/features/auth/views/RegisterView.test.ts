// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { ref } from 'vue'
import PrimeVue from 'primevue/config'
import { createMemoryHistory, createRouter } from 'vue-router'
import RegisterView from './RegisterView.vue'

const mocks = vi.hoisted(() => ({ registrationStatus: vi.fn() }))
vi.mock('/logo.svg', () => ({ default: '/logo.svg' }))
vi.mock('../services/auth.service', () => ({ authService: mocks }))
vi.mock('../composables/useAuth', () => ({ useAuth: () => ({
  register: vi.fn(), isLoading: ref(false), error: ref(null), clearError: vi.fn(),
}) }))

async function mountView() {
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/', component: RegisterView }] })
  const wrapper = mount(RegisterView, { global: { plugins: [router, PrimeVue], stubs: {
    Card: { template: '<div><slot name="header" /><slot name="content" /></div>' },
    RouterLink: { template: '<a><slot /></a>' }, CloudflareTurnstile: true,
  } } })
  await flushPromises()
  return wrapper
}

describe('registration availability', () => {
  afterEach(() => vi.clearAllMocks())
  it('shows the pause message and keeps sign-in navigation without signup controls', async () => {
    mocks.registrationStatus.mockResolvedValue({ enabled: false,
      message: 'Public registration is temporarily paused while we prepare Automaton for wider access.' })
    const wrapper = await mountView()
    expect(wrapper.text()).toContain('Public registration is temporarily paused while we prepare Automaton for wider access.')
    expect(wrapper.text()).toContain('Sign in')
    expect(wrapper.find('form').exists()).toBe(false)
    expect(wrapper.text()).not.toContain('Continue with GitHub')
  })
  it('restores signup when the API configuration enables it', async () => {
    mocks.registrationStatus.mockResolvedValue({ enabled: true, message: null })
    const wrapper = await mountView()
    expect(wrapper.find('form').exists()).toBe(true)
    expect(wrapper.text()).toContain('Continue with GitHub')
  })
  it('keeps sign-in available when the status request fails', async () => {
    mocks.registrationStatus.mockRejectedValue(new Error('offline'))
    const wrapper = await mountView()
    expect(wrapper.text()).toContain('Unable to check registration availability')
    expect(wrapper.text()).toContain('Sign in')
    expect(wrapper.find('form').exists()).toBe(false)
  })
})
