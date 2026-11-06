// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'

const getCatalogSources = vi.fn()
const addCatalogSource = vi.fn()
const setCatalogSourceEnabled = vi.fn()
const removeCatalogSource = vi.fn()
vi.mock('../services/apps.service', () => ({
  appsService: {
    getCatalogSources: () => getCatalogSources(),
    addCatalogSource: (...a: unknown[]) => addCatalogSource(...a),
    setCatalogSourceEnabled: (...a: unknown[]) => setCatalogSourceEnabled(...a),
    removeCatalogSource: (...a: unknown[]) => removeCatalogSource(...a),
  },
}))

import PrimeVue from 'primevue/config'
import CatalogSources from './CatalogSources.vue'

const official = { id: 'official', name: 'Official', repo: 'rzvdaniel/automaton-apps', branch: 'catalog', official: true, enabled: true, hasToken: false }
const custom = { id: 'abc', name: 'Acme', repo: 'acme/apps', branch: 'catalog', official: false, enabled: true, hasToken: true }
const mountSources = () => mount(CatalogSources, { global: { plugins: [PrimeVue] } })

describe('CatalogSources', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    getCatalogSources.mockResolvedValue([official, custom])
  })

  it('lists the sources, warns about trust, and never offers to remove the official one', async () => {
    const wrapper = mountSources()
    await flushPromises()
    expect(wrapper.text()).toContain('Only add sources you trust')
    expect(wrapper.text()).toContain('acme/apps')
    expect(wrapper.text()).toContain('Private')
    expect(wrapper.find('[aria-label="Remove Official"]').exists()).toBe(false)
    expect(wrapper.find('[aria-label="Remove Acme"]').exists()).toBe(true)
  })

  it('adds a source with its token, clears the token field and tells the catalog to reload', async () => {
    addCatalogSource.mockResolvedValue(custom)
    const wrapper = mountSources()
    await flushPromises()
    await wrapper.find('[aria-label="Source repository"]').setValue('https://github.com/acme/apps')
    await wrapper.find('input.catalog-token').setValue('github_pat_x')
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(addCatalogSource).toHaveBeenCalledWith({ repo: 'https://github.com/acme/apps', branch: undefined, name: undefined, token: 'github_pat_x' })
    expect((wrapper.find('input.catalog-token').element as HTMLInputElement).value).toBe('')
    expect(wrapper.emitted('changed')).toHaveLength(1)
  })

  it('shows the server message when a source cannot be added', async () => {
    addCatalogSource.mockRejectedValue(Object.assign(new Error('x'), { isAxiosError: true, response: { data: { message: 'GitHub answered 404 for acme/apps' } } }))
    const wrapper = mountSources()
    await flushPromises()
    await wrapper.find('[aria-label="Source repository"]').setValue('acme/apps')
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(wrapper.text()).toContain('GitHub answered 404 for acme/apps')
    expect(wrapper.emitted('changed')).toBeUndefined()
  })

  it('removes a custom source', async () => {
    removeCatalogSource.mockResolvedValue(undefined)
    const wrapper = mountSources()
    await flushPromises()
    await wrapper.find('[aria-label="Remove Acme"]').trigger('click')
    await flushPromises()
    expect(removeCatalogSource).toHaveBeenCalledWith('abc')
    expect(wrapper.emitted('changed')).toHaveLength(1)
  })
})
