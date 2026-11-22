// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import PrimeVue from 'primevue/config'
import { createMemoryHistory, createRouter } from 'vue-router'
import { describe, expect, it, vi } from 'vitest'
import AdminLayout from './AdminLayout.vue'

vi.mock('../services/adminAppsService', () => ({ adminAppsService: { listApps: vi.fn().mockResolvedValue({ waitingForApprovalTotal: 0 }) } }))

async function mountAt(path: string) {
  const page = { template: '<div />' }
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/admin', component: AdminLayout, children: [{ path: ':rest(.*)*', component: page }] }],
  })
  await router.push(path)
  await router.isReady()
  const wrapper = mount({ template: '<RouterView />' }, { global: { plugins: [router, createPinia(), PrimeVue] } })
  await flushPromises()
  return wrapper
}

describe('AdminLayout', () => {
  it('shows the Administration menu on admin pages', async () => {
    expect((await mountAt('/admin/apps')).find('.admin-sidebar').exists()).toBe(true)
  })

  it('hides it on Backups and user pages, which have their own place in the main menu', async () => {
    expect((await mountAt('/admin/backups')).find('.admin-sidebar').exists()).toBe(false)
    expect((await mountAt('/admin/users/u1')).find('.admin-sidebar').exists()).toBe(false)
  })
})
