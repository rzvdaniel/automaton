// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import ToastService from 'primevue/toastservice'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AppListItem } from '../types/apps.types'
import AppListView from './AppListView.vue'
import { useAppsStore } from '../stores/apps.store'

const push = vi.fn()
const toastAdd = vi.hoisted(() => vi.fn())
vi.mock('primevue/usetoast', () => ({ useToast: () => ({ add: toastAdd }) }))
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
  canDelete: true, canUpdate: true, runtime: { genericRuntimeDeployed: true }, liveEnvironment: live, ...over,
})

async function mountView(apps: AppListItem[]) {
  setActivePinia(createPinia())
  const store = useAppsStore()
  store.fetchApps = vi.fn(async () => { store.listApps = apps })
  const wrapper = mount(AppListView, { global: { plugins: [PrimeVue, ToastService] } })
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

  it('lists each app as a link to its page with kind once and only the state', async () => {
    const wrapper = await mountView(apps)
    expect(wrapper.find('h1').text()).toBe('Apps')
    const links = wrapper.findAll('.apps-card a')
    expect(links.map((link) => link.attributes('href'))).toEqual(['/apps/a', '/apps/b', '/apps/c'])
    expect(links[0]!.text()).toContain('Web app · Take notes')
    expect(links[0]!.text()).not.toContain('private')
    expect(links[0]!.text()).toContain('live')
    expect(links[1]!.text()).toContain('Action')
    expect(links[1]!.text()).not.toContain('action ')
    expect(links[2]!.text()).toContain('stopped')
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
    await wrapper.find('.apps-empty button').trigger('click')
    expect(rows(wrapper)).toHaveLength(3)
  })

  it('labels the search box for screen readers', async () => {
    const wrapper = await mountView(apps)
    expect(wrapper.find('label.apps-search .sr-only').text()).toBe('Search apps')
  })

  it('shows the shared apps in the same row style', async () => {
    const wrapper = await mountView(apps)
    expect(wrapper.find('section[aria-label="Shared with you"]').text()).toContain('Shared Tool')
    const row = wrapper.find('section .app-row')
    expect(row.element.tagName).toBe('BUTTON')
  })

  it('pauses and resumes an app from its row and shows the Paused state', async () => {
    const wrapper = await mountView([item({ id: 'a', name: 'Notes' }), item({ id: 'p', name: 'Backup', isPaused: true })])
    const store = useAppsStore()
    store.setAppPaused = vi.fn(async (): Promise<string[]> => [])
    const rowsText = wrapper.findAll('.apps-card .app-row').map((row) => row.text())
    expect(rowsText[0]).toContain('live')
    expect(rowsText[0]).toContain('Pause')
    expect(rowsText[1]).toContain('paused')
    expect(rowsText[1]).toContain('Resume')

    await wrapper.find('button[aria-label="Pause Notes"]').trigger('click')
    await wrapper.find('button[aria-label="Resume Backup"]').trigger('click')

    expect(store.setAppPaused).toHaveBeenNthCalledWith(1, 'a', true)
    expect(store.setAppPaused).toHaveBeenNthCalledWith(2, 'p', false)
  })

  it('shows a warning toast instead of success when pausing returns warnings', async () => {
    const wrapper = await mountView([item({ id: 'a', name: 'Notes' })])
    const store = useAppsStore()
    store.setAppPaused = vi.fn(async () => ['The Runner could not be told to stop the app.'])
    toastAdd.mockClear()
    await wrapper.find('button[aria-label="Pause Notes"]').trigger('click')
    await flushPromises()
    expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'warn', summary: 'Paused with warnings' }))
    expect(toastAdd).not.toHaveBeenCalledWith(expect.objectContaining({ severity: 'success' }))
  })

  it('hides Pause and Resume when the person cannot update the app', async () => {
    const wrapper = await mountView([item({ id: 'a', name: 'Notes', canUpdate: false })])
    expect(wrapper.find('button[aria-label="Pause Notes"]').exists()).toBe(false)
  })

  it('shows a friendly empty state with both buttons when there are no apps', async () => {
    assigned.mockResolvedValue([])
    const wrapper = await mountView([])
    expect(wrapper.text()).toContain('No apps yet')
    const buttons = wrapper.find('.apps-empty').findAll('button').map((b) => b.text())
    expect(buttons).toEqual(['From the catalog', 'New app'])
  })

  it('opens the New app dialog and routes to the catalog when asked', async () => {
    const wrapper = await mountView(apps)
    const dialog = wrapper.findComponent({ name: 'NewAppDialog' })
    expect(dialog.props('visible')).toBe(false)
    await wrapper.findAll('button').find((b) => b.text() === 'New app')!.trigger('click')
    expect(dialog.props('visible')).toBe(true)
    dialog.vm.$emit('catalog')
    expect(push).toHaveBeenCalledWith({ name: 'apps-catalog' })
  })
})
