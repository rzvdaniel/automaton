// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import HomeView from './HomeView.vue'
import { useAuthStore } from '@/features/auth/stores/auth.store'

const push = vi.fn()
vi.mock('primevue/usetoast', () => ({ useToast: () => ({ add: vi.fn() }) }))
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
  RouterLink: { props: ['to'], template: '<a :href="to"><slot /></a>' },
}))
const consoleService = vi.hoisted(() => ({ install: vi.fn(), get: vi.fn() }))
vi.mock('@/features/server/services/server-console.service', () => ({ serverConsoleService: consoleService }))
const service = vi.hoisted(() => ({ get: vi.fn(), pin: vi.fn(), unpin: vi.fn(), reorder: vi.fn(), dismiss: vi.fn(), setSize: vi.fn(), setView: vi.fn() }))
vi.mock('../services/home.service', () => ({ homeService: service }))

const adminApps = vi.hoisted(() => ({ listApps: vi.fn(), setPermission: vi.fn() }))
vi.mock('@/features/admin/services/adminAppsService', () => ({ adminAppsService: adminApps }))

const notifications = vi.hoisted(() => ({ list: vi.fn(), acknowledge: vi.fn() }))
vi.mock('../services/notifications.service', () => ({ notificationsService: notifications }))

const app = (id: string, name: string) => ({
  id, name, summary: '', description: '', tags: [], isPublished: false, updatedAtUtc: '', canDelete: false,
  runtime: { genericRuntimeDeployed: true }, runner: null,
  liveEnvironment: { name: 'Live', status: 'Running', isHostedRunner: false, runnerOnline: true, genericRuntimeDeployed: true, routePath: '', lastSuccessfulDeploymentAtUtc: null },
})
const card = (key: string, dismissible: boolean) => ({
  key, kind: key, severity: 'info', title: `Card ${key}`, detail: 'detail', actionLabel: 'Do it', actionPath: '/server', dismissible,
})

function mountHome(roles: string[] = ['User']) {
  const auth = useAuthStore()
  auth.user = { roles } as never
  return mount(HomeView, { global: { plugins: [PrimeVue] } })
}

describe('HomeView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    notifications.list.mockResolvedValue([])
  })

  it('shows Needs-you cards and pinned apps in the saved order', async () => {
    service.get.mockResolvedValue({
      needsYou: [card('no-runner', false), card('no-smtp', true)],
      pinned: [{ app: app('1', 'Shop'), pinnedAtUtc: '' }, { app: app('2', 'Blog'), pinnedAtUtc: '' }],
    })
    const wrapper = mountHome()
    await flushPromises()
    expect(wrapper.findAll('[data-testid^="need-"]')).toHaveLength(2)
    expect(wrapper.findAll('[data-testid="dismiss-card"]')).toHaveLength(1)
    expect(wrapper.findAll('[data-testid="pinned-app"]').map((el) => el.find('.pin__name').text())).toEqual(['Shop', 'Blog'])
    expect(wrapper.find('[data-testid="home-empty"]').exists()).toBe(false)
  })

  it('names each waiting request on the permission card and approves it there', async () => {
    const requests = { ...card('permission-requests', false), title: 'An app asks for permissions', actionLabel: 'Review requests', actionPath: '/admin/permission-requests' }
    service.get.mockResolvedValue({ needsYou: [requests], pinned: [] })
    adminApps.listApps
      .mockResolvedValueOnce({ items: [{ id: 'a1', name: 'e2e-heartbeat', waitingPermissions: ['notify:email'] }], total: 1, waitingForApprovalTotal: 1 })
      .mockResolvedValue({ items: [], total: 0, waitingForApprovalTotal: 0 })
    adminApps.setPermission.mockResolvedValue([])
    const wrapper = mountHome(['SysAdmin'])
    await flushPromises()
    expect(wrapper.find('[data-testid="home-permission-requests"]').text()).toContain('e2e-heartbeat wants to send email (notify:email)')
    await wrapper.find('[aria-label="Approve notify:email for e2e-heartbeat"]').trigger('click')
    await flushPromises()
    expect(adminApps.setPermission).toHaveBeenCalledWith('a1', 'notify:email', true)
    expect(wrapper.find('[data-testid="home-permission-requests"]').exists()).toBe(false)
  })

  it('shows three requests and "See all N" when more wait', async () => {
    const requests = { ...card('permission-requests', false), actionLabel: 'Review requests' }
    service.get.mockResolvedValue({ needsYou: [requests], pinned: [] })
    adminApps.listApps.mockResolvedValue({
      items: ['a', 'b', 'c', 'd'].map((id) => ({ id, name: `app-${id}`, waitingPermissions: ['notify:email'] })), total: 4, waitingForApprovalTotal: 4,
    })
    const wrapper = mountHome(['SysAdmin'])
    await flushPromises()
    expect(wrapper.findAll('[data-testid="permission-request"]')).toHaveLength(3)
    expect(wrapper.text()).toContain('See all 4')
  })

  it('installs Server Console from the recommended card, then the card offers Open in a new tab', async () => {
    const recommended = { ...card('recommended:server-console', true), kind: 'recommended-app', title: 'Install Server Console', catalogAppId: 'server-console', actionLabel: 'Install' }
    service.get.mockResolvedValue({ needsYou: [recommended], pinned: [] })
    consoleService.install.mockResolvedValue({ appId: 'abc', running: true })
    const open = vi.spyOn(window, 'open').mockReturnValue(null)
    const wrapper = mountHome(['SysAdmin'])
    await flushPromises()
    await wrapper.find('[data-testid="need-recommended-app"] button').trigger('click')
    await flushPromises()
    expect(consoleService.install).toHaveBeenCalledOnce()
    expect(push).not.toHaveBeenCalled()
    const need = wrapper.find('[data-testid="need-recommended-app"]')
    expect(need.text()).toContain('Server Console is installed')
    expect(need.find('button').text()).toContain('Open')
    expect(wrapper.find('[data-testid="dismiss-card"]').exists()).toBe(false)
    await need.find('button').trigger('click')
    expect(open).toHaveBeenCalledWith(expect.stringContaining('/apps/abc/application'), '_blank', 'noopener')
    expect(consoleService.install).toHaveBeenCalledOnce()
    open.mockRestore()
  })

  it('shows Installing… after the install until the app is running, then offers Open', async () => {
    vi.useFakeTimers()
    try {
      const recommended = { ...card('recommended:server-console', true), kind: 'recommended-app', title: 'Install Server Console', catalogAppId: 'server-console', actionLabel: 'Install' }
      service.get.mockResolvedValue({ needsYou: [recommended], pinned: [] })
      consoleService.install.mockResolvedValue({ appId: 'abc', running: false })
      consoleService.get.mockResolvedValueOnce({ appId: 'abc', running: false }).mockResolvedValue({ appId: 'abc', running: true })
      const open = vi.spyOn(window, 'open').mockReturnValue(null)
      const wrapper = mountHome(['SysAdmin'])
      await flushPromises()
      await wrapper.find('[data-testid="need-recommended-app"] button').trigger('click')
      await flushPromises()
      const button = () => wrapper.find('[data-testid="need-recommended-app"] button')
      expect(button().text()).toContain('Installing…')
      await button().trigger('click')
      expect(open).not.toHaveBeenCalled()
      await vi.advanceTimersByTimeAsync(3000)
      expect(button().text()).toContain('Installing…')
      await vi.advanceTimersByTimeAsync(3000)
      expect(button().text()).toContain('Open')
      open.mockRestore()
    } finally {
      vi.useRealTimers()
    }
  })

  it('dismisses an optional card for good', async () => {
    service.get.mockResolvedValue({ needsYou: [card('no-smtp', true)], pinned: [] })
    service.dismiss.mockResolvedValue(undefined)
    const wrapper = mountHome()
    await flushPromises()
    await wrapper.find('[data-testid="dismiss-card"]').trigger('click')
    await flushPromises()
    expect(service.dismiss).toHaveBeenCalledWith('no-smtp')
    expect(wrapper.find('[data-testid="need-no-smtp"]').exists()).toBe(false)
  })

  it('unpins an app and saves a new order', async () => {
    service.get.mockResolvedValue({ needsYou: [], pinned: [{ app: app('1', 'Shop'), pinnedAtUtc: '' }, { app: app('2', 'Blog'), pinnedAtUtc: '' }] })
    service.unpin.mockResolvedValue(undefined)
    service.reorder.mockResolvedValue(undefined)
    const wrapper = mountHome()
    await flushPromises()
    await wrapper.find('[aria-label="Move Shop later"]').trigger('click')
    await flushPromises()
    expect(service.reorder).toHaveBeenCalledWith(['2', '1'])
    await wrapper.find('[data-testid="unpin"]').trigger('click')
    await flushPromises()
    expect(service.unpin).toHaveBeenCalledTimes(1)
    expect(wrapper.findAll('[data-testid="pinned-app"]')).toHaveLength(1)
  })

  it('suggests pinning when empty, and Server Console only to a system administrator', async () => {
    service.get.mockResolvedValue({ needsYou: [], pinned: [] })
    const member = mountHome()
    await flushPromises()
    expect(member.find('[data-testid="home-empty"]').text()).not.toContain('Server Console')
    const admin = mountHome(['SysAdmin'])
    await flushPromises()
    expect(admin.find('[data-testid="home-empty"]').text()).toContain('Server Console')
  })

  it('shows notifications, lets a warning be acknowledged and keeps a critical one', async () => {
    const item = (key: string, severity: string, acknowledgeable: boolean) => ({
      key, kind: key, severity, title: `Notice ${key}`, detail: 'd', actionLabel: 'Open', actionPath: '/server', count: 1, subjects: [], acknowledgeable,
    })
    service.get.mockResolvedValue({ needsYou: [], pinned: [] })
    notifications.list.mockResolvedValue([item('runner-offline', 'critical', false), item('certificate-expiring', 'warning', true)])
    notifications.acknowledge.mockResolvedValue(undefined)
    const wrapper = mountHome(['SysAdmin'])
    await flushPromises()
    expect(wrapper.findAll('[data-testid^="notification-"]')).toHaveLength(2)
    expect(wrapper.findAll('[data-testid="acknowledge-notification"]')).toHaveLength(1)
    await wrapper.find('[data-testid="acknowledge-notification"]').trigger('click')
    await flushPromises()
    expect(notifications.acknowledge).toHaveBeenCalledWith('certificate-expiring')
    expect(wrapper.findAll('[data-testid^="notification-"]')).toHaveLength(1)
    expect(wrapper.find('[data-testid="notification-runner-offline"]').exists()).toBe(true)
  })

  it('shows an app card as plain text and greys it with its time when it is stale', async () => {
    const html = '<img src=x onerror=alert(1)>'
    const fresh = { card: { status: html, facts: [{ label: 'Disk', value: '61%' }], alerts: [{ severity: 'critical', text: 'Backup is late' }] }, updatedAtUtc: new Date().toISOString(), stale: false }
    service.get.mockResolvedValue({
      needsYou: [],
      pinned: [
        { app: app('1', 'Server Console'), pinnedAtUtc: '', card: fresh },
        { app: app('2', 'Blog'), pinnedAtUtc: '', card: { ...fresh, stale: true } },
        { app: app('3', 'Plain'), pinnedAtUtc: '', card: null },
      ],
    })
    const wrapper = mountHome()
    await flushPromises()
    const cards = wrapper.findAll('[data-testid="app-card"]')
    expect(cards).toHaveLength(2)
    expect(cards[0]!.find('[data-testid="app-card-status"]').text()).toBe(html)
    expect(cards[0]!.find('img').exists()).toBe(false)
    expect(cards[0]!.text()).toContain('Disk')
    expect(cards[0]!.find('[data-testid="app-card-alert"]').text()).toContain('Backup is late')
    expect(cards[0]!.classes()).not.toContain('hc--stale')
    expect(cards[1]!.classes()).toContain('hc--stale')
    expect(cards[1]!.find('[data-testid="app-card-time"]').text()).toContain('Not up to date')
  })

  it('makes a card wide, switches to the list view and announces a move', async () => {
    service.get.mockResolvedValue({
      needsYou: [], listView: false,
      pinned: [{ app: app('1', 'Shop'), pinnedAtUtc: '' }, { app: app('2', 'Blog'), pinnedAtUtc: '' }],
    })
    service.setSize.mockResolvedValue(undefined)
    service.setView.mockResolvedValue(undefined)
    service.reorder.mockResolvedValue(undefined)
    const wrapper = mountHome()
    await flushPromises()
    await wrapper.findAll('[data-testid="toggle-wide"]')[0]!.trigger('click')
    expect(service.setSize).toHaveBeenCalledWith('1', true)
    expect(wrapper.findAll('[data-testid="pinned-app"]')[0]!.classes()).toContain('pin--wide')

    await wrapper.find('[aria-label="Move Shop later"]').trigger('click')
    await flushPromises()
    expect(service.reorder).toHaveBeenCalledWith(['2', '1'])
    expect(wrapper.find('[data-testid="move-announcement"]').text()).toBe('Shop moved to position 2 of 2')

    await wrapper.find('[data-testid="view-list"]').trigger('click')
    await flushPromises()
    expect(service.setView).toHaveBeenCalledWith(true)
    expect(wrapper.findAll('[data-testid="pinned-row"]')).toHaveLength(2)
    expect(wrapper.find('[data-testid="pinned-app"]').exists()).toBe(false)
  })
})
