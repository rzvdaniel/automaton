// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import { beforeAll, describe, expect, it, vi } from 'vitest'
import PrimeVue from 'primevue/config'
import Tooltip from 'primevue/tooltip'
import AppVersionHistory from './AppVersionHistory.vue'

vi.mock('primevue/usetoast', () => ({
  useToast: () => ({ add: vi.fn() }),
}))

vi.mock('../services/apps.service', () => ({
  appsService: {
    getVersions: vi.fn().mockResolvedValue({
      versions: [
        {
          id: 'snapshot-id',
          versionNumber: 3,
          trigger: 'ManualSave',
          summary: 'Before local changes',
          createdAt: '2026-09-15T10:00:00Z',
          createdBy: 'Test user',
        },
      ],
      totalCount: 1,
      pageNumber: 1,
      pageSize: 20,
    }),
    getVersionDetail: vi.fn(),
  },
}))

beforeAll(() => {
  vi.stubGlobal('IntersectionObserver', class {
    observe() {}
    disconnect() {}
  })
})

describe('AppVersionHistory', () => {
  it('shows source snapshot history without source mutation controls', async () => {
    const wrapper = mount(AppVersionHistory, {
      props: { appId: 'app-id', sourceAvailable: true, fullPage: true },
      global: {
        plugins: [PrimeVue],
        directives: { tooltip: Tooltip },
      },
    })
    await flushPromises()

    expect(wrapper.text()).toContain('Source snapshots')
    expect(wrapper.text()).toContain('Snapshot 3')
    expect(wrapper.text()).toContain('automaton pull app-id')
    expect(wrapper.text()).toContain('current app source, not a historical snapshot')
    expect(wrapper.text()).not.toContain('Restore source')
    expect(wrapper.text()).not.toContain('Create source snapshot')
  })
})
