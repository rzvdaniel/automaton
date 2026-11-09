// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { reactive } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import EmailNotConfiguredNotice from './EmailNotConfiguredNotice.vue'
import { installService } from '../services/install.service'

const auth = vi.hoisted(() => ({ value: { isAuthenticated: true, user: { roles: ['SysAdmin'] as string[] } } }))
vi.mock('@/features/auth/stores/auth.store', () => ({ useAuthStore: () => auth.value }))
const route = reactive({ fullPath: '/' })
vi.mock('vue-router', () => ({ useRoute: () => route }))
vi.mock('../services/install.service', () => ({ installService: { getSummary: vi.fn() } }))

const RouterLinkStub = { props: ['to'], template: '<a :data-to="to"><slot /></a>' }
const mountNotice = () => mount(EmailNotConfiguredNotice, { global: { plugins: [PrimeVue], stubs: { RouterLink: RouterLinkStub } } })
const summary = (emailConfigured?: boolean | null) =>
  vi.mocked(installService.getSummary).mockResolvedValue({ version: null, users: 1, apps: 0, runners: 0, lastBackupAt: null, emailConfigured })

describe('EmailNotConfiguredNotice', () => {
  beforeEach(() => {
    vi.mocked(installService.getSummary).mockReset()
    auth.value = { isAuthenticated: true, user: { roles: ['SysAdmin'] } }
  })

  it('tells a system administrator that email is not set up and links to the docs', async () => {
    summary(false)
    const wrapper = mountNotice()
    await flushPromises()

    const notice = wrapper.get('[data-testid=email-not-configured]')
    expect(notice.text()).toContain('Email is not set up')
    expect(notice.html()).toContain('https://automaton.monster/docs/install-server#set-up-email')
  })

  it('shows nothing once email is configured, or when an older server does not say', async () => {
    summary(true)
    const configured = mountNotice()
    await flushPromises()
    expect(configured.find('[data-testid=email-not-configured]').exists()).toBe(false)
    summary(undefined)
    const older = mountNotice()
    await flushPromises()
    expect(older.find('[data-testid=email-not-configured]').exists()).toBe(false)
  })

  it('goes away on the next page change after email has been saved', async () => {
    summary(false)
    const wrapper = mountNotice()
    await flushPromises()
    expect(wrapper.find('[data-testid=email-not-configured]').exists()).toBe(true)

    summary(true)
    route.fullPath = '/settings'
    await flushPromises()

    expect(wrapper.find('[data-testid=email-not-configured]').exists()).toBe(false)
  })

  it('shows nothing to other users and does not ask the server', async () => {
    auth.value = { isAuthenticated: true, user: { roles: ['Admin'] } }
    summary(false)
    const wrapper = mountNotice()
    await flushPromises()

    expect(wrapper.find('[data-testid=email-not-configured]').exists()).toBe(false)
    expect(installService.getSummary).not.toHaveBeenCalled()
  })

  it('shows nothing when the summary cannot be read', async () => {
    vi.mocked(installService.getSummary).mockRejectedValue(new Error('403'))
    const wrapper = mountNotice()
    await flushPromises()

    expect(wrapper.find('[data-testid=email-not-configured]').exists()).toBe(false)
  })
})
