// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AppListItem } from '../types/apps.types'
import AppListView from './AppListView.vue'
import { useAppsStore } from '../stores/apps.store'

const push = vi.fn()
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
  useRoute: () => ({ meta: {} }),
  RouterLink: { props: ['to'], template: '<a :href="to"><slot /></a>' },
}))
const assigned = vi.hoisted(() => vi.fn())
vi.mock('@/features/organizations/services/organizationsService', () => ({
  organizationsService: { getMyAssignedApps: assigned },
}))
vi.mock('../services/apps.service', () => ({ appsService: {} }))
vi.mock('../components/CatalogPanel.vue', () => ({ default: { template: '<div />' } }))

const live = { name: 'Live', status: 'Running', isHostedRunner: false, runnerOnline: true, genericRuntimeDeployed: true, routePath: '/' }
const item = (over: Partial<AppListItem>): AppListItem => ({
  id: 'x', name: 'X', summary: '', description: '', tags: [], isPublished: true, updatedAtUtc: '2026-01-01T00:00:00Z',
  canDelete: true, runtime: { genericRuntimeDeployed: true }, liveEnvironment: live, ...over,
})

async function mountView(apps: AppListItem[]) {
  setActivePinia(createPinia())
  const store = useAppsStore()
  store.fetchApps = vi.fn(async () => { store.listApps = apps })
  const wrapper = mount(AppListView, { global: { plugins: [PrimeVue] } })
  await flushPromises()
  return wrapper
}

const rows = (wrapper: Awaited<ReturnType<typeof mountView>>) => wrapper.findAll('.apps-card a').map((row) => row.text())

describe('AppListView', () => {
  beforeEach(() => {
    push.mockClear()
    assigned.mockResolvedValue([{ id: 'sh1', name: 'Shared Tool', isPublished: false }])
  })

  const apps = [
    item({ id: 'a', name: 'Notes', summary: 'Take notes' }),
    item({ id: 'b', name: 'Nightly', actionsOnly: true }),
    item({ id: 'c', name: 'Idle', liveEnvironment: null, runner: null }),
  ]

  it('lists each app as a link to its page with kind, access and state', async () => {
    const wrapper = await mountView(apps)
    expect(wrapper.find('h1').text()).toBe('Apps')
    const links = wrapper.findAll('.apps-card a')
    expect(links.map((link) => link.attributes('href'))).toEqual(['/apps/a', '/apps/b', '/apps/c'])
    expect(links[0].text()).toContain('Web app · Take notes')
    expect(links[0].text()).toContain('private')
    expect(links[0].text()).toContain('live')
    expect(links[1].text()).toContain('action')
    expect(links[2].text()).toContain('stopped')
    expect(wrapper.text()).toContain('All · 3')
  })

  it('filters with the pills and the search box', async () => {
    const wrapper = await mountView(apps)
    const pill = (label: string) => wrapper.findAll('.apps-pill').find((p) => p.text() === label)!
    await pill('Actions').trigger('click')
    expect(rows(wrapper)).toHaveLength(1)
    expect(rows(wrapper)[0]).toContain('Nightly')
    await pill('Stopped').trigger('click')
    expect(rows(wrapper)[0]).toContain('Idle')
    await pill('All · 3').trigger('click')
    await wrapper.find('input[type="search"]').setValue('note')
    expect(rows(wrapper)).toHaveLength(1)
    await wrapper.find('input[type="search"]').setValue('zzz')
    expect(wrapper.text()).toContain('No apps match')
  })

  it('labels the search box for screen readers', async () => {
    const wrapper = await mountView(apps)
    expect(wrapper.find('label.apps-search .sr-only').text()).toBe('Search apps')
  })

  it('shows the shared apps in the same row style', async () => {
    const wrapper = await mountView(apps)
    expect(wrapper.find('section[aria-label="Shared with you"]').text()).toContain('Shared Tool')
    expect(wrapper.find('section .app-row').exists()).toBe(true)
  })

  it('shows a friendly empty state with both buttons when there are no apps', async () => {
    assigned.mockResolvedValue([])
    const wrapper = await mountView([])
    expect(wrapper.text()).toContain('No apps yet')
    const buttons = wrapper.find('.apps-empty').findAll('button').map((b) => b.text())
    expect(buttons).toEqual(['From the catalog', 'New app'])
  })
})
