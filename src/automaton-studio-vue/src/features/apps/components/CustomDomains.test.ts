// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import CustomDomains from './CustomDomains.vue'
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

const stubs = {
  InputText: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template: `<input :value="modelValue" @input="$emit('update:modelValue', $event.target.value)">`,
  },
}
const waiting = { target: 'apps.test', targetAddresses: ['203.0.113.10'], domains: [{ id: 'd1', host: 'blog.example.com', isPrimary: false, verified: false }] }
const verified = { ...waiting, domains: [{ id: 'd1', host: 'blog.example.com', isPrimary: true, verified: true }] }

function open() {
  return mount(CustomDomains, { props: { appId: 'app-1' }, global: { stubs } })
}

describe('CustomDomains', () => {
  beforeEach(() => vi.resetAllMocks())

  it('shows the DNS record to create for a domain that is waiting', async () => {
    vi.mocked(appsService.getCustomDomains).mockResolvedValue(waiting)
    const wrapper = open()
    await flushPromises()
    const dns = wrapper.get('[data-testid="custom-domain-dns"]').text()
    expect(dns).toContain('CNAME')
    expect(dns).toContain('apps.test')
    expect(dns).toContain('203.0.113.10')
  })

  it('adds a domain and clears the field', async () => {
    vi.mocked(appsService.getCustomDomains).mockResolvedValue({ target: 'apps.test', targetAddresses: [], domains: [] })
    vi.mocked(appsService.addCustomDomain).mockResolvedValue(waiting)
    const wrapper = open()
    await flushPromises()
    await wrapper.get('[data-testid="custom-domain-input"]').setValue('blog.example.com')
    await wrapper.get('[data-testid="custom-domain-add"]').trigger('click')
    await flushPromises()
    expect(appsService.addCustomDomain).toHaveBeenCalledWith('app-1', 'blog.example.com')
    expect(wrapper.findAll('[data-testid="custom-domain"]')).toHaveLength(1)
  })

  it('checks DNS and shows the domain as primary', async () => {
    vi.mocked(appsService.getCustomDomains).mockResolvedValue(waiting)
    vi.mocked(appsService.verifyCustomDomain).mockResolvedValue(verified)
    const wrapper = open()
    await flushPromises()
    await wrapper.get('[data-testid="custom-domain-verify"]').trigger('click')
    await flushPromises()
    expect(wrapper.text()).toContain('Primary address')
  })

  it('shows the server message when DNS does not point here yet', async () => {
    vi.mocked(appsService.getCustomDomains).mockResolvedValue(waiting)
    vi.mocked(appsService.verifyCustomDomain).mockRejectedValue({ response: { data: { message: 'blog.example.com does not point at this server yet.' } } })
    const wrapper = open()
    await flushPromises()
    await wrapper.get('[data-testid="custom-domain-verify"]').trigger('click')
    await flushPromises()
    expect(wrapper.get('[data-testid="custom-domain-error"]').text()).toContain('does not point at this server')
  })

  it('removes a domain', async () => {
    vi.mocked(appsService.getCustomDomains).mockResolvedValue(verified)
    vi.mocked(appsService.removeCustomDomain).mockResolvedValue({ ...verified, domains: [] })
    const wrapper = open()
    await flushPromises()
    await wrapper.get('[data-testid="custom-domain-remove"]').trigger('click')
    await flushPromises()
    expect(wrapper.findAll('[data-testid="custom-domain"]')).toHaveLength(0)
  })
})
