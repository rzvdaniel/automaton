// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'

const getCatalog = vi.fn()
const resolveGitUrl = vi.fn()
const push = vi.fn()
const replace = vi.fn()
const routeQuery = vi.hoisted(() => ({ value: {} as Record<string, string> }))
const roles = vi.hoisted(() => ({ value: [] as string[] }))
vi.mock('@/features/auth/stores/auth.store', () => ({ useAuthStore: () => ({ user: { roles: roles.value } }) }))
vi.mock('../services/apps.service', () => ({ appsService: { getCatalog: (...a: unknown[]) => getCatalog(...a), resolveGitUrl: (...a: unknown[]) => resolveGitUrl(...a) } }))
vi.mock('vue-router', async () => ({ ...(await vi.importActual<object>('vue-router')), useRouter: () => ({ push, replace }), useRoute: () => ({ query: routeQuery.value }) }))

import source from './CatalogPanel.vue?raw'
import PrimeVue from 'primevue/config'
import CatalogPanel from './CatalogPanel.vue'

const RouterLink = { props: ['to'], template: '<a class="link" :data-to="JSON.stringify(to)"><slot /></a>' }
const entry = { id: 'backup', name: 'Off-site Backup', summary: 'Copies backups away', category: 'Maintenance', repo: 'o/apps', folder: 'm/backup', commit: '0123456789abcdef0123456789abcdef01234567', minServerVersion: '0.2.31', permissions: ['backups:read'], requiresNewerServer: false, installedAppId: null, sourceId: 'official', sourceName: 'Official', customSource: false }
const mountPanel = () => mount(CatalogPanel, { attachTo: document.body, global: { plugins: [PrimeVue], stubs: { RouterLink, CatalogSources: { template: '<div class="sources-stub" />' } } } })
const button = (label: string) => document.body.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`) ?? Array.from(document.body.querySelectorAll('button')).find((b) => b.textContent?.trim() === label)

describe('CatalogPanel', () => {
  beforeEach(() => {
    window.matchMedia ??= (() => ({ matches: false, addEventListener: () => {}, removeEventListener: () => {} })) as unknown as typeof window.matchMedia; vi.clearAllMocks(); roles.value = []; routeQuery.value = {} })

  it('lists catalog entries with source, commit, permissions and an Install link pinned to the commit', async () => {
    getCatalog.mockResolvedValue({ apps: [entry], fetchedAt: null, stale: false, unavailable: false, message: null })
    const wrapper = mountPanel()
    await flushPromises()
    expect(wrapper.text()).toContain('Off-site Backup')
    expect(wrapper.text()).toContain('commit 0123456')
    expect(wrapper.text()).toContain('backups:read')
    expect(wrapper.find('.link').attributes('data-to')).toContain('"commit":"0123456789abcdef0123456789abcdef01234567"')
  })

  it('shows Installed with a Manage link for an installed entry', async () => {
    getCatalog.mockResolvedValue({ apps: [{ ...entry, installedAppId: 'app-1' }], fetchedAt: null, stale: false, unavailable: false, message: null })
    const wrapper = mountPanel()
    await flushPromises()
    expect(wrapper.text()).toContain('Installed')
    expect(wrapper.text()).toContain('Manage')
    expect(wrapper.find('.link').attributes('data-to')).toContain('app-1')
  })

  it('warns when the list is the saved copy', async () => {
    getCatalog.mockResolvedValue({ apps: [entry], fetchedAt: null, stale: true, unavailable: false, message: 'The catalog source could not be reached.' })
    const wrapper = mountPanel()
    await flushPromises()
    expect(wrapper.text()).toContain('could not be reached')
    expect(wrapper.text()).toContain('Off-site Backup')
  })

  it('shows the empty state with Retry when nothing was ever read, and Retry asks for a refresh', async () => {
    getCatalog.mockResolvedValue({ apps: [], fetchedAt: null, stale: false, unavailable: true, message: 'The catalog source could not be reached.' })
    const wrapper = mountPanel()
    await flushPromises()
    expect(wrapper.text()).toContain('The catalog could not be loaded')
    await wrapper.findAll('button').find((b) => b.text() === 'Retry')!.trigger('click')
    expect(getCatalog).toHaveBeenLastCalledWith(true, expect.objectContaining({ page: 1 }))
  })

  it('marks entries that need a newer server instead of offering Install', async () => {
    getCatalog.mockResolvedValue({ apps: [{ ...entry, requiresNewerServer: true }], fetchedAt: null, stale: false, unavailable: false, message: null })
    const wrapper = mountPanel()
    await flushPromises()
    expect(wrapper.text()).toContain('Needs Automaton Server 0.2.31')
    expect(wrapper.find('.link').exists()).toBe(false)
  })

  it('keeps the Git address form out of the list until Install from URL is opened, then pins it', async () => {
    roles.value = ['SysAdmin']
    getCatalog.mockResolvedValue({ apps: [], fetchedAt: null, stale: false, unavailable: false, message: null })
    resolveGitUrl.mockResolvedValue({ repo: 'o/r', folder: '', commit: 'a'.repeat(40) })
    const wrapper = mountPanel()
    await flushPromises()
    expect(document.body.querySelector('input[aria-label="Git address"]')).toBeNull()
    button('Install from URL')!.click()
    await flushPromises()
    const input = document.body.querySelector<HTMLInputElement>('input[aria-label="Git address"]')!
    input.value = 'https://github.com/o/r'
    input.dispatchEvent(new Event('input'))
    await flushPromises()
    document.body.querySelector('form')!.dispatchEvent(new Event('submit'))
    await flushPromises()
    expect(resolveGitUrl).toHaveBeenCalledWith('https://github.com/o/r')
    expect(push).toHaveBeenCalledWith({ name: 'app-install-source', query: { repo: 'o/r', folder: undefined, commit: 'a'.repeat(40) } })
    wrapper.unmount()
  })

  it('styles panels and links with the Studio theme tokens, not undefined legacy ones', () => {
    expect(source).toContain('var(--p-content-background)')
    expect(source).toContain('var(--p-primary-color)')
    expect(source).not.toMatch(/var\(--surface-/)
  })

  it('labels entries from a custom source with its name', async () => {
    getCatalog.mockResolvedValue({ apps: [{ ...entry, customSource: true, sourceName: 'Acme store' }], fetchedAt: null, stale: false, unavailable: false, message: null })
    const wrapper = mountPanel()
    await flushPromises()
    expect(wrapper.text()).toContain('Source: Acme store')
  })

  it('offers Manage sources only to a system administrator and opens the sources panel from it', async () => {
    getCatalog.mockResolvedValue({ apps: [entry], fetchedAt: null, stale: false, unavailable: false, message: null })
    const plain = mountPanel()
    await flushPromises()
    expect(button('Manage sources')).toBeUndefined()
    expect(button('Install from URL')).toBeUndefined()
    plain.unmount()
    roles.value = ['SysAdmin']
    const admin = mountPanel()
    await flushPromises()
    expect(document.body.querySelector('.sources-stub')).toBeNull()
    button('Manage sources')!.click()
    await flushPromises()
    expect(document.body.querySelector('.sources-stub')).not.toBeNull()
    admin.unmount()
  })

  const many = (count: number) => Array.from({ length: count }, (_, i) => ({ ...entry, id: `a${i}`, name: `App ${i}`, folder: `m/a${i}` }))
  const facets = { categories: [{ value: 'Maintenance', label: 'Maintenance', count: 10 }, { value: 'Tools', label: 'Tools', count: 20 }], sources: [{ value: 'official', label: 'Official', count: 30 }] }
  const big = (extra: object = {}) => ({ apps: many(24), fetchedAt: null, stale: false, unavailable: false, message: null, total: 30, totalApps: 30, page: 1, pageSize: 24, ...facets, ...extra })

  it('hides the filters and the pager while the catalog has 12 apps or fewer', async () => {
    getCatalog.mockResolvedValue({ ...big({ apps: many(3), total: 3, totalApps: 3 }) })
    const wrapper = mountPanel()
    await flushPromises()
    expect(wrapper.find('input[aria-label="Search apps"]').exists()).toBe(false)
    expect(wrapper.text()).not.toContain('Installed')
    expect(wrapper.find('.p-paginator').exists()).toBe(false)
  })

  it('shows search, category chips, source, Installed toggle and a pager once the catalog is bigger than 12', async () => {
    getCatalog.mockResolvedValue(big())
    const wrapper = mountPanel()
    await flushPromises()
    expect(wrapper.find('input[aria-label="Search apps"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('Maintenance (10)')
    expect(wrapper.text()).toContain('Installed')
    expect(wrapper.find('.catalog-source').text()).toContain('All sources')
    expect(wrapper.find('.p-paginator').exists()).toBe(true)
  })

  it('asks the server for the filters in the address and keeps them in the query string', async () => {
    routeQuery.value = { q: 'back', category: 'Tools', source: 'official', installed: '1', page: '2' }
    getCatalog.mockResolvedValue(big({ page: 2 }))
    const wrapper = mountPanel()
    await flushPromises()
    expect(getCatalog).toHaveBeenCalledWith(false, { q: 'back', category: 'Tools', source: 'official', installed: true, page: 2, pageSize: 24 })
    expect((wrapper.find('input[aria-label="Search apps"]').element as HTMLInputElement).value).toBe('back')
    await wrapper.findAll('button').find((b) => b.text() === 'Maintenance (10)')!.trigger('click')
    expect(replace).toHaveBeenCalledWith({ query: { q: 'back', category: 'Maintenance', source: 'official', installed: '1' } })
  })

  it('waits for a pause in typing before searching', async () => {
    vi.useFakeTimers()
    getCatalog.mockResolvedValue(big())
    const wrapper = mountPanel()
    await flushPromises()
    await wrapper.find('input[aria-label="Search apps"]').setValue('back')
    expect(replace).not.toHaveBeenCalled()
    vi.advanceTimersByTime(350)
    expect(replace).toHaveBeenCalledWith({ query: { q: 'back' } })
    vi.useRealTimers()
  })

  it('says nothing matches and clears the filters', async () => {
    routeQuery.value = { q: 'zzz' }
    getCatalog.mockResolvedValue(big({ apps: [], total: 0 }))
    const wrapper = mountPanel()
    await flushPromises()
    expect(wrapper.text()).toContain('No apps match')
    await wrapper.findAll('button').find((b) => b.text() === 'Clear filters')!.trigger('click')
    expect(replace).toHaveBeenCalledWith({ query: {} })
  })
})
