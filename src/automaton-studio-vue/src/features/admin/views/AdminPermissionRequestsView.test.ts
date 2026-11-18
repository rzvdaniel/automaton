// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import AdminPermissionRequestsView from './AdminPermissionRequestsView.vue'

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {} }),
  RouterLink: { props: ['to'], template: '<a :href="to"><slot /></a>' },
}))
const service = vi.hoisted(() => ({ listApps: vi.fn(), setPermission: vi.fn() }))
vi.mock('../services/adminAppsService', () => ({ adminAppsService: service }))

const one = { items: [{ id: 'a1', name: 'e2e-heartbeat', waitingPermissions: ['notify:email', 'backups:read'] }], total: 1, waitingForApprovalTotal: 1 }

describe('AdminPermissionRequestsView', () => {
  beforeEach(() => vi.clearAllMocks())

  it('lists requests grouped by app with what each allows', async () => {
    service.listApps.mockResolvedValue(one)
    const wrapper = mount(AdminPermissionRequestsView, { global: { plugins: [PrimeVue] } })
    await flushPromises()
    expect(wrapper.findAll('[data-testid="request-group"]')).toHaveLength(1)
    expect(wrapper.findAll('[data-testid="permission-request"]')).toHaveLength(2)
    expect(wrapper.text()).toContain('e2e-heartbeat wants to send email (notify:email)')
    expect(wrapper.text()).toContain('Lets the app email people')
  })

  it('approves all of an app, then shows the empty state', async () => {
    service.listApps.mockResolvedValueOnce(one).mockResolvedValue({ items: [], total: 0, waitingForApprovalTotal: 0 })
    service.setPermission.mockResolvedValue([])
    const wrapper = mount(AdminPermissionRequestsView, { global: { plugins: [PrimeVue] } })
    await flushPromises()
    await wrapper.find('[aria-label="Approve all 2 requests of e2e-heartbeat"]').trigger('click')
    await flushPromises()
    expect(service.setPermission).toHaveBeenCalledTimes(2)
    expect(service.setPermission).toHaveBeenCalledWith('a1', 'backups:read', true)
    expect(wrapper.find('[data-testid="requests-empty"]').text()).toBe('No app is waiting for you.')
  })

  it('rejects a single request', async () => {
    service.listApps.mockResolvedValue(one)
    service.setPermission.mockResolvedValue([])
    const wrapper = mount(AdminPermissionRequestsView, { global: { plugins: [PrimeVue] } })
    await flushPromises()
    await wrapper.find('[aria-label="Reject notify:email for e2e-heartbeat"]').trigger('click')
    await flushPromises()
    expect(service.setPermission).toHaveBeenCalledWith('a1', 'notify:email', false)
  })
})
