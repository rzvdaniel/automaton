// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import ConfirmationService from 'primevue/confirmationservice'
import ToastService from 'primevue/toastservice'
import { createPinia, setActivePinia } from 'pinia'
import AppAdminTab from './AppAdminTab.vue'
import { adminAppsService } from '../services/adminAppsService'
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

const waiting = [{ permission: 'backups:run', approved: false, status: 'Pending' }]

function open(role: string) {
  setActivePinia(createPinia())
  useAuthStore().user = { roles: [role] } as never
  return mount(AppAdminTab, {
    props: { appId: 'app-1', appName: 'Console' },
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

  it('lists waiting requests first and folds long approved lists behind Show more', async () => {
    const many = Array.from({ length: 8 }, (_, index) => ({ permission: `p${index}:use`, approved: true, status: 'Approved' }))
    vi.mocked(adminAppsService.getPermissions).mockResolvedValue([...many, { permission: 'backups:run', approved: false, status: 'Pending' }] as never)
    const wrapper = open('SysAdmin')
    await flushPromises()
    expect(wrapper.get('[data-testid="admin-waiting"]').text()).toContain('backups:run')
    expect(wrapper.get('[data-testid="admin-summary"]').text()).toContain('1 waiting · 8 approved')
    expect(wrapper.text()).toContain('p4:use')
    expect(wrapper.text()).not.toContain('p5:use')
    await wrapper.get('[data-testid="admin-show-more"]').trigger('click')
    expect(wrapper.text()).toContain('p7:use')
  })

  it('shows the owner without a Pause button, and leaves approvals to system administrators', async () => {
    const wrapper = open('Admin')
    await flushPromises()
    expect(wrapper.find('[data-testid="admin-permissions"]').exists()).toBe(false)
    expect(adminAppsService.getPermissions).not.toHaveBeenCalled()
    expect(wrapper.get('[data-testid="admin-moderation"]').text()).toContain('Ana')
    expect(wrapper.find('[data-testid="pause-app"]').exists()).toBe(false)
  })
})
