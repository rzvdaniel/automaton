// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { ref } from 'vue'
import PrimeVue from 'primevue/config'
import { createMemoryHistory, createRouter } from 'vue-router'
import LoginView from './LoginView.vue'
import CliAuthorizeView from './CliAuthorizeView.vue'

const mocks = vi.hoisted(() => ({
  getStatus: vi.fn(),
  details: vi.fn(),
  approve: vi.fn(),
  cancel: vi.fn(),
}))
vi.mock('@/features/install/services/install.service', () => ({ installService: { getStatus: mocks.getStatus } }))
vi.mock('../services/auth.service', () => ({ authService: { startGitHubLogin: vi.fn() } }))
vi.mock('../composables/useAuth', () => ({ useAuth: () => ({
  login: vi.fn(), isLoading: ref(false), error: ref(null), clearError: vi.fn(),
}) }))
vi.mock('../services/cli-authorization.service', () => ({
  getCliAuthorizationDetails: mocks.details,
  approveCliAuthorization: mocks.approve,
  cancelCliAuthorization: mocks.cancel,
}))

async function mountAt(component: object, url: string) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/:p(.*)*', component }],
  })
  await router.push(url)
  await router.isReady()
  const wrapper = mount(component, { global: { plugins: [router, PrimeVue] } })
  await flushPromises()
  return { wrapper, router }
}

describe('sign-in page', () => {
  afterEach(() => vi.clearAllMocks())

  it('shows the headline, labelled fields, the invite hint and the agent note', async () => {
    mocks.getStatus.mockResolvedValue({ installationComplete: true, gitHubEnabled: false, userSignUp: false })
    const { wrapper } = await mountAt(LoginView, '/login')
    const text = wrapper.text()
    expect(text).toContain('All your AI-built apps, on your server.')
    expect(text).toContain("New here? Ask your server's admin for an invite.")
    expect(text).toContain('Signing in from your AI agent? Approve the code it shows you after signing in.')
    expect(wrapper.find('label[for="email"]').exists()).toBe(true)
    expect(wrapper.find('label[for="password"]').exists()).toBe(true)
    expect(text).toContain('Forgot password?')
    expect(text).not.toContain('Continue with GitHub')
    expect(text).not.toContain('Sign up')
  })

  it('offers GitHub only when the server has it configured', async () => {
    mocks.getStatus.mockResolvedValue({ installationComplete: true, gitHubEnabled: true, userSignUp: false })
    const { wrapper } = await mountAt(LoginView, '/login')
    expect(wrapper.text()).toContain('Continue with GitHub')
  })
})

describe('agent approval page', () => {
  afterEach(() => vi.clearAllMocks())

  it('shows the code in boxes and approves or denies', async () => {
    mocks.details.mockResolvedValue({
      deviceName: 'e2e-laptop', remoteIp: '10.0.0.1', createdAt: '2026-01-01T00:00:00Z', expiresAt: '', status: 'pending',
    })
    mocks.approve.mockResolvedValue(undefined)
    mocks.cancel.mockResolvedValue(undefined)
    const { wrapper, router } = await mountAt(CliAuthorizeView, '/cli/authorize?requestId=r1&code=ABCD-1234')
    expect(wrapper.text()).toContain('Let your AI agent in?')
    expect(wrapper.findAll('.cli-code-char').map((c) => c.text()).join('')).toBe('ABCD-1234')
    expect(wrapper.text()).toContain('API Keys')

    const buttons = wrapper.findAll('button')
    const approve = buttons.find((b) => b.text() === 'Approve')!
    await approve.trigger('click')
    await flushPromises()
    expect(mocks.approve).toHaveBeenCalledWith('r1', 'ABCD-1234')
    expect(wrapper.text()).toContain('Automaton CLI is authorized on this device.')

    const deny = (await mountAt(CliAuthorizeView, '/cli/authorize?requestId=r1&code=ABCD-1234')).wrapper
      .findAll('button').find((b) => b.text() === 'Deny')!
    await deny.trigger('click')
    await flushPromises()
    expect(mocks.cancel).toHaveBeenCalledWith('r1', 'ABCD-1234')
    expect(router).toBeTruthy()
  })

  it('says so when the link has no code', async () => {
    const { wrapper } = await mountAt(CliAuthorizeView, '/cli/authorize')
    expect(wrapper.text()).toContain('This authorization link is incomplete. Restart automaton login.')
  })
})
