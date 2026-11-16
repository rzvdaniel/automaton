// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import AppDomainTab from './AppDomainTab.vue'
import { appsService } from '../services/apps.service'

vi.mock('../services/apps.service', () => ({
  appsService: {
    getCustomDomains: vi.fn(),
    addCustomDomain: vi.fn(),
    verifyCustomDomain: vi.fn(),
    setPrimaryCustomDomain: vi.fn(),
    removeCustomDomain: vi.fn(),
  },
}))

const domain = { id: 'd1', host: 'blog.example.com', isPrimary: false, verified: false, live: false, status: 'WaitingForDns' as const, challengeHost: '_automaton-challenge.blog.example.com', challengeValue: 'automaton-verify=abc' }
const waiting = { target: 'apps.test', targetAddresses: ['203.0.113.10'], domains: [domain] }
const steps = (statuses: string[]) => ['txt', 'points', 'certificate'].map((key, index) => ({ key, title: key, status: statuses[index], detail: `${key} detail` }))

function open(isPublic = true) {
  return mount(AppDomainTab, { props: { appId: 'app-1', appName: 'blog', isPublic, publicHost: 'blog.apps.test' }, global: { plugins: [PrimeVue] } })
}

describe('AppDomainTab', () => {
  beforeEach(() => vi.resetAllMocks())

  it('asks to make the app public first when it is not', async () => {
    const wrapper = open(false)
    await flushPromises()
    expect(wrapper.find('[data-testid="domain-not-public"]').exists()).toBe(true)
    expect(appsService.getCustomDomains).not.toHaveBeenCalled()
    await wrapper.find('[data-testid="domain-not-public"] button').trigger('click')
    expect(wrapper.emitted('share')).toHaveLength(1)
  })

  it('shows the records with relative names and the full names below', async () => {
    vi.mocked(appsService.getCustomDomains).mockResolvedValue(waiting)
    const wrapper = open()
    await flushPromises()
    const table = wrapper.get('[data-testid="dns-records"]').text()
    expect(table).toContain('TXT')
    expect(table).toContain('_automaton-challenge.blog')
    expect(table).toContain('automaton-verify=abc')
    expect(table).toContain('CNAME')
    expect(table).toContain('apps.test')
    const text = wrapper.text()
    expect(text).toContain('_automaton-challenge.blog.example.com')
    expect(text).toContain('DNS only')
    expect(text).toContain('203.0.113.10')
  })

  it('adds a domain and clears the field', async () => {
    vi.mocked(appsService.getCustomDomains).mockResolvedValue({ ...waiting, domains: [] })
    vi.mocked(appsService.addCustomDomain).mockResolvedValue(waiting)
    const wrapper = open()
    await flushPromises()
    await wrapper.get('[data-testid="custom-domain-input"]').setValue('blog.example.com')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(appsService.addCustomDomain).toHaveBeenCalledWith('app-1', 'blog.example.com')
    expect(wrapper.findAll('[data-testid="custom-domain"]')).toHaveLength(1)
  })

  it('shows each step and a live message when the check passes', async () => {
    vi.mocked(appsService.getCustomDomains).mockResolvedValue(waiting)
    vi.mocked(appsService.verifyCustomDomain).mockResolvedValue({ ...waiting, domains: [{ ...domain, verified: true, live: true, status: 'Live', isPrimary: true }], check: steps(['ok', 'ok', 'ok']) } as never)
    const wrapper = open()
    await flushPromises()
    await wrapper.get('[data-testid="custom-domain-verify"]').trigger('click')
    await flushPromises()
    expect(wrapper.findAll('[data-testid="check-steps"] li')).toHaveLength(3)
    expect(wrapper.get('[data-testid="check-live"]').text()).toContain('blog.example.com is live')
    expect(wrapper.text()).toContain('Live · primary address')
  })

  it('says the certificate is pending when DNS is right but the domain is not live yet', async () => {
    vi.mocked(appsService.getCustomDomains).mockResolvedValue(waiting)
    vi.mocked(appsService.verifyCustomDomain).mockResolvedValue({ ...waiting, domains: [{ ...domain, verified: true, status: 'Verified' }], check: steps(['ok', 'ok', 'skipped']) } as never)
    const wrapper = open()
    await flushPromises()
    await wrapper.get('[data-testid="custom-domain-verify"]').trigger('click')
    await flushPromises()
    expect(wrapper.find('[data-testid="check-live"]').exists()).toBe(false)
    expect(wrapper.get('[data-testid="check-waiting"]').text()).toContain('certificate is being issued')
    expect(wrapper.text()).toContain('Verified · waiting for the certificate')
  })

  it('shows the failing step and the server message when the check fails', async () => {
    vi.mocked(appsService.getCustomDomains).mockResolvedValue(waiting)
    vi.mocked(appsService.verifyCustomDomain).mockRejectedValue({ response: { data: { message: 'blog.example.com does not point at this server yet.', steps: steps(['ok', 'failed', 'skipped']) } } })
    const wrapper = open()
    await flushPromises()
    await wrapper.get('[data-testid="custom-domain-verify"]').trigger('click')
    await flushPromises()
    expect(wrapper.findAll('.step--failed')).toHaveLength(1)
    expect(wrapper.get('[data-testid="check-failed"]').text()).toContain('does not point at this server yet')
    expect(wrapper.text()).toContain('Not pointing here yet')
  })
})
