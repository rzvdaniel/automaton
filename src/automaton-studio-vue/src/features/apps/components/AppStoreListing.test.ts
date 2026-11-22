// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import ToastService from 'primevue/toastservice'
import AppStoreListing from './AppStoreListing.vue'
import { appsService } from '../services/apps.service'
import type { App, AppSharing } from '../types/apps.types'

vi.mock('../services/apps.service', () => ({
  appsService: {
    get: vi.fn(),
    getMarketplaceCategories: vi.fn(),
    setSharing: vi.fn(),
    updateListing: vi.fn(),
    reorderScreenshots: vi.fn(),
    deleteScreenshot: vi.fn(),
  },
}))

const app = { id: 'app-1', name: 'blog' } as App
const sharing = (audience: AppSharing['audience'], listed = false) => ({ audience, people: [], publicUrlSlug: 'blog', listed }) as AppSharing
const shots = [{ id: 's1', imageUrl: '/a.png', sortOrder: 0 }, { id: 's2', imageUrl: '/b.png', sortOrder: 1 }]

function open(state: AppSharing) {
  return mount(AppStoreListing, { props: { app, sharing: state }, global: { plugins: [PrimeVue, ToastService] } })
}

describe('AppStoreListing', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    vi.mocked(appsService.get).mockResolvedValue({ summary: 'A blog', category: 'Publishing', tags: 'a', thumbnailUrl: null, screenshots: shots } as never)
    vi.mocked(appsService.getMarketplaceCategories).mockResolvedValue(['Publishing', 'Tools'])
  })

  it('cannot list a private app and points to Share', async () => {
    const wrapper = open(sharing('me'))
    await flushPromises()
    expect(wrapper.get('[data-testid="list-toggle"] input').attributes('disabled')).toBeDefined()
    expect(wrapper.find('[data-testid="listing-not-public"]').exists()).toBe(true)
  })

  it('lists a public app in the store from the toggle', async () => {
    vi.mocked(appsService.setSharing).mockResolvedValue(sharing('internet', true))
    const wrapper = open(sharing('internet'))
    await flushPromises()
    await wrapper.get('[data-testid="list-toggle"] input').trigger('change')
    await flushPromises()
    expect(appsService.setSharing).toHaveBeenCalledWith('app-1', 'internet', 'blog', undefined, true)
    expect(wrapper.emitted('changed')).toHaveLength(1)
  })

  it('saves the summary and category and previews them on the store card', async () => {
    const wrapper = open(sharing('internet', true))
    await flushPromises()
    await wrapper.get('[data-testid="listing-summary"]').setValue('New summary')
    expect(wrapper.get('[data-testid="listing-preview"]').text()).toContain('New summary')
    await wrapper.get('[data-testid="listing-save"]').trigger('click')
    await flushPromises()
    expect(appsService.updateListing).toHaveBeenCalledWith('app-1', { summary: 'New summary', category: 'Publishing', tags: ['a'] })
  })

  it('reorders and removes screenshots through the API', async () => {
    vi.mocked(appsService.reorderScreenshots).mockResolvedValue()
    vi.mocked(appsService.deleteScreenshot).mockResolvedValue()
    const wrapper = open(sharing('internet', true))
    await flushPromises()
    await wrapper.get('[aria-label="Move screenshot 1 later"]').trigger('click')
    await flushPromises()
    expect(appsService.reorderScreenshots).toHaveBeenCalledWith('app-1', ['s2', 's1'])
    await wrapper.get('[aria-label="Remove screenshot 1"]').trigger('click')
    await flushPromises()
    expect(appsService.deleteScreenshot).toHaveBeenCalledWith('app-1', 's2')
  })
})
