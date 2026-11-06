// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'

const getCatalog = vi.fn()
const resolveGitUrl = vi.fn()
const push = vi.fn()
const roles = vi.hoisted(() => ({ value: [] as string[] }))
vi.mock('@/features/auth/stores/auth.store', () => ({ useAuthStore: () => ({ user: { roles: roles.value } }) }))
vi.mock('../services/apps.service', () => ({ appsService: { getCatalog: (...a: unknown[]) => getCatalog(...a), resolveGitUrl: (...a: unknown[]) => resolveGitUrl(...a) } }))
vi.mock('vue-router', async () => ({ ...(await vi.importActual<object>('vue-router')), useRouter: () => ({ push }) }))

import source from './CatalogPanel.vue?raw'
import PrimeVue from 'primevue/config'
import CatalogPanel from './CatalogPanel.vue'

const RouterLink = { props: ['to'], template: '<a class="link" :data-to="JSON.stringify(to)"><slot /></a>' }
const entry = { id: 'backup', name: 'Off-site Backup', summary: 'Copies backups away', category: 'Maintenance', repo: 'o/apps', folder: 'm/backup', commit: '0123456789abcdef0123456789abcdef01234567', minServerVersion: '0.2.31', permissions: ['backups:read'], requiresNewerServer: false, installedAppId: null, sourceId: 'official', sourceName: 'Official', customSource: false }
const mountPanel = () => mount(CatalogPanel, { global: { plugins: [PrimeVue], stubs: { RouterLink, CatalogSources: { template: '<div class="sources-stub" />' } } } })

describe('CatalogPanel', () => {
  beforeEach(() => { vi.clearAllMocks(); roles.value = [] })

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
    expect(getCatalog).toHaveBeenLastCalledWith(true)
  })

  it('marks entries that need a newer server instead of offering Install', async () => {
    getCatalog.mockResolvedValue({ apps: [{ ...entry, requiresNewerServer: true }], fetchedAt: null, stale: false, unavailable: false, message: null })
    const wrapper = mountPanel()
    await flushPromises()
    expect(wrapper.text()).toContain('Needs Automaton Server 0.2.31')
    expect(wrapper.find('.link').exists()).toBe(false)
  })

  it('pins a pasted Git address and opens the install page', async () => {
    getCatalog.mockResolvedValue({ apps: [], fetchedAt: null, stale: false, unavailable: false, message: null })
    resolveGitUrl.mockResolvedValue({ repo: 'o/r', folder: '', commit: 'a'.repeat(40) })
    const wrapper = mountPanel()
    await flushPromises()
    await wrapper.find('input').setValue('https://github.com/o/r')
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(resolveGitUrl).toHaveBeenCalledWith('https://github.com/o/r')
    expect(push).toHaveBeenCalledWith({ name: 'app-install-source', query: { repo: 'o/r', folder: undefined, commit: 'a'.repeat(40) } })
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

  it('shows the source management only to a system administrator', async () => {
    getCatalog.mockResolvedValue({ apps: [entry], fetchedAt: null, stale: false, unavailable: false, message: null })
    const plain = mountPanel()
    await flushPromises()
    expect(plain.find('.sources-stub').exists()).toBe(false)
    roles.value = ['SysAdmin']
    const admin = mountPanel()
    await flushPromises()
    expect(admin.find('.sources-stub').exists()).toBe(true)
  })
})
