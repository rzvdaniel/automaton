// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import { describe, expect, it, vi } from 'vitest'
import SharedAppsSection from './SharedAppsSection.vue'

const createRuntimeLaunch = vi.fn().mockResolvedValue({ runtimeToken: '', runtimeUrl: 'https://app-a1.apps.example.test/' })
vi.mock('../services/apps.service', () => ({
  appsService: {
    createRuntimeLaunch: (id: string) => createRuntimeLaunch(id),
  },
}))

const mountSection = (apps: { id: string; name: string; isPublished: boolean; summary?: string }[]) =>
  mount(SharedAppsSection, { props: { apps }, global: { plugins: [PrimeVue] } })

describe('SharedAppsSection', () => {
  it('lists the apps shared with the user', () => {
    const wrapper = mountSection([{ id: 'a1', name: 'Site Links', isPublished: false, summary: 'Short links' }])
    expect(wrapper.text()).toContain('Shared with you')
    expect(wrapper.text()).toContain('Site Links')
    expect(wrapper.text()).toContain('Short links')
  })

  it('renders nothing when no app is shared', () => {
    expect(mountSection([]).find('section').exists()).toBe(false)
  })

  it('starts a restricted runtime launch when the user opens an app', async () => {
    const assign = vi.fn()
    vi.stubGlobal('location', { ...window.location, assign })
    const wrapper = mountSection([{ id: 'a1', name: 'Site Links', isPublished: false }])
    await wrapper.find('button').trigger('click')
    await flushPromises()
    expect(createRuntimeLaunch).toHaveBeenCalledWith('a1')
    expect(assign).toHaveBeenCalledWith('https://app-a1.apps.example.test/')
    vi.unstubAllGlobals()
  })
})
