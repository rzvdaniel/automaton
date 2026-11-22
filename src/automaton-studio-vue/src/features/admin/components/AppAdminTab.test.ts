// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import ConfirmationService from 'primevue/confirmationservice'
import ToastService from 'primevue/toastservice'
import { createPinia, setActivePinia } from 'pinia'
import AppAdminTab from './AppAdminTab.vue'
import { adminAppsService } from '../services/adminAppsService'
import { appsService } from '@/features/apps/services/apps.service'
import { useAuthStore } from '@/features/auth/stores/auth.store'

vi.mock('../services/adminAppsService', () => ({
  adminAppsService: {
    getApp: vi.fn(),
    getPermissions: vi.fn(),
    setPermission: vi.fn(),
    getRunnerPermissions: vi.fn().mockResolvedValue([]),
    setRunnerPermission: vi.fn(),
    getRootFileTree: vi.fn().mockResolvedValue([]),
    readRootFile: vi.fn(),
    updateModeration: vi.fn(),
  },
}))
vi.mock('@/features/apps/services/apps.service', () => ({ appsService: { setPaused: vi.fn() } }))

const waiting = [{ permission: 'backups:run', approved: false, status: 'Pending' }]

function open(role: string, paused = false) {
  setActivePinia(createPinia())
  useAuthStore().user = { roles: [role] } as never
  return mount(AppAdminTab, {
    props: { appId: 'app-1', appName: 'Console', paused },
    global: { plugins: [PrimeVue, ToastService, ConfirmationService], stubs: { Dialog: true, Select: true, Textarea: true } },
  })
}

describe('AppAdminTab', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(adminAppsService.getApp).mockResolvedValue({ app: { reviewStatus: 'approved', accessStatus: 'active' }, ownerName: 'Ana', ownerEmail: 'ana@example.test' } as never)
    vi.mocked(adminAppsService.getPermissions).mockResolvedValue(waiting)
  })

  it('lets a system administrator approve or deny a waiting permission', async () => {
    vi.mocked(adminAppsService.setPermission).mockResolvedValue([{ permission: 'backups:run', approved: true, status: 'Approved' }])
    const wrapper = open('SysAdmin')
    await flushPromises()
    expect(wrapper.text()).toContain('backups:run')
    expect(wrapper.find('[aria-label="Deny backups:run"]').exists()).toBe(true)

    await wrapper.get('[aria-label="Approve backups:run"]').trigger('click')
    await flushPromises()

    expect(adminAppsService.setPermission).toHaveBeenCalledWith('app-1', 'backups:run', true)
    expect(wrapper.text()).toContain('Approved')
  })

  it('shows the owner and the Pause button, and leaves approvals to system administrators', async () => {
    const wrapper = open('Admin')
    await flushPromises()
    expect(wrapper.find('[data-testid="admin-permissions"]').exists()).toBe(false)
    expect(adminAppsService.getPermissions).not.toHaveBeenCalled()
    expect(wrapper.get('[data-testid="admin-moderation"]').text()).toContain('Ana')
    expect(wrapper.get('[data-testid="pause-app"]').text()).toContain('Pause app')
  })

  it('resumes a paused app without asking', async () => {
    vi.mocked(appsService.setPaused).mockResolvedValue({} as never)
    const wrapper = open('SysAdmin', true)
    await flushPromises()
    await wrapper.get('[data-testid="pause-app"]').trigger('click')
    await flushPromises()
    expect(appsService.setPaused).toHaveBeenCalledWith('app-1', false, undefined)
    expect(wrapper.emitted('paused-changed')).toHaveLength(1)
  })

  it('pauses with the reason the administrator typed', async () => {
    vi.mocked(appsService.setPaused).mockResolvedValue({} as never)
    const wrapper = open('SysAdmin')
    await flushPromises()
    await wrapper.get('[data-testid="pause-app"]').trigger('click');
    (wrapper.findComponent('[data-testid="pause-reason"]') as VueWrapper).vm.$emit('update:modelValue', 'Sends spam')
    await flushPromises()
    await wrapper.get('[data-testid="pause-confirm"]').trigger('click')
    await flushPromises()
    expect(appsService.setPaused).toHaveBeenCalledWith('app-1', true, 'Sends spam')
    expect(wrapper.emitted('paused-changed')).toHaveLength(1)
  })
})
