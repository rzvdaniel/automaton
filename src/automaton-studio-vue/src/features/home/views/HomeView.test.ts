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
const service = vi.hoisted(() => ({ get: vi.fn(), pin: vi.fn(), unpin: vi.fn(), reorder: vi.fn(), dismiss: vi.fn() }))
vi.mock('../services/home.service', () => ({ homeService: service }))

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
})
