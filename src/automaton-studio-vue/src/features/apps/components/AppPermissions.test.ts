// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import AppPermissions from './AppPermissions.vue'
import { useAuthStore } from '@/features/auth/stores/auth.store'

const getPermissions = vi.hoisted(() => vi.fn())
const setPermission = vi.hoisted(() => vi.fn())
const getRunnerPermissions = vi.hoisted(() => vi.fn())
const setRunnerPermission = vi.hoisted(() => vi.fn())
vi.mock('../services/apps.service', () => ({ appsService: { getPermissions } }))
vi.mock('@/features/admin/services/adminAppsService', () => ({ adminAppsService: { setPermission, getRunnerPermissions, setRunnerPermission } }))

async function mountAs(roles: string[]) {
  setActivePinia(createPinia())
  useAuthStore().user = { roles } as never
  const wrapper = mount(AppPermissions, { props: { appId: 'a1' }, global: { plugins: [PrimeVue] } })
  await flushPromises()
  return wrapper
}

describe('AppPermissions', () => {
  beforeEach(() => {
    setPermission.mockReset().mockResolvedValue([])
    getRunnerPermissions.mockReset().mockResolvedValue([])
    setRunnerPermission.mockReset().mockResolvedValue([])
    getPermissions.mockReset().mockResolvedValue({
      permissions: [{ permission: 'notify:email', status: 'Pending' }],
      waiting: ['notify:email'],
      approvalUrl: 'https://x/admin/apps/a1',
    })
  })

  it('lets a system administrator approve and reloads', async () => {
    const wrapper = await mountAs(['SysAdmin'])
    await wrapper.find('button[aria-label="Approve notify:email"]').trigger('click')
    await flushPromises()
    expect(setPermission).toHaveBeenCalledWith('a1', 'notify:email', true)
    expect(getPermissions).toHaveBeenCalledTimes(2)
  })

  it('lets a system administrator reject', async () => {
    const wrapper = await mountAs(['SysAdmin'])
    await wrapper.find('button[aria-label="Reject notify:email"]').trigger('click')
    expect(setPermission).toHaveBeenCalledWith('a1', 'notify:email', false)
  })

  it('shows no buttons to an owner who is not a system administrator', async () => {
    const wrapper = await mountAs(['User'])
    expect(wrapper.find('button').exists()).toBe(false)
    expect(wrapper.text()).toContain('A system administrator of this server must approve')
  })

  it('lets a system administrator approve runner:maintain one machine at a time', async () => {
    getPermissions.mockResolvedValue({ permissions: [{ permission: 'runner:maintain', status: 'Approved' }], waiting: [], approvalUrl: null })
    getRunnerPermissions.mockResolvedValue([{ runnerId: 'r1', name: 'runner-01', approved: false }])
    setRunnerPermission.mockResolvedValue([{ runnerId: 'r1', name: 'runner-01', approved: true }])
    const wrapper = await mountAs(['SysAdmin'])
    await wrapper.find('button[aria-label="Approve maintenance on runner-01"]').trigger('click')
    await flushPromises()
    expect(setRunnerPermission).toHaveBeenCalledWith('a1', 'r1', true)
    expect(wrapper.text()).toContain('approved')
  })

  it('shows no machine approvals to an owner who is not a system administrator', async () => {
    getPermissions.mockResolvedValue({ permissions: [{ permission: 'runner:maintain', status: 'Approved' }], waiting: [], approvalUrl: null })
    const wrapper = await mountAs(['User'])
    expect(getRunnerPermissions).not.toHaveBeenCalled()
    expect(wrapper.text()).not.toContain('each machine')
  })
})
