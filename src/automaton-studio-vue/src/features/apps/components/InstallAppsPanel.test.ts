// @vitest-environment jsdom
import { describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'

const getInstallableApps = vi.fn()
vi.mock('../services/apps.service', () => ({ appsService: { getInstallableApps: () => getInstallableApps() } }))

import PrimeVue from 'primevue/config'
import InstallAppsPanel from './InstallAppsPanel.vue'

const RouterLink = { props: ['to'], template: '<a class="link" :data-to="JSON.stringify(to)"><slot /></a>' }

describe('InstallAppsPanel', () => {
  it('lists installable apps with an Install link to the install page', async () => {
    getInstallableApps.mockResolvedValue([{ id: 'a1', name: 'Off-site Backup', summary: 'Copies backups away' }])
    const wrapper = mount(InstallAppsPanel, { global: { plugins: [PrimeVue], stubs: { RouterLink } } })
    await flushPromises()
    expect(wrapper.text()).toContain('Off-site Backup')
    expect(wrapper.text()).toContain('Copies backups away')
    expect(wrapper.find('.link').attributes('data-to')).toContain('a1')
  })

  it('says so when nothing is installable', async () => {
    getInstallableApps.mockResolvedValue([])
    const wrapper = mount(InstallAppsPanel, { global: { plugins: [PrimeVue], stubs: { RouterLink } } })
    await flushPromises()
    expect(wrapper.text()).toContain('No installable apps')
  })
})
