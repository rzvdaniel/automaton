// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import { beforeAll, describe, expect, it, vi } from 'vitest'
import PrimeVue from 'primevue/config'
import Tooltip from 'primevue/tooltip'
import AppVersionHistory from './AppVersionHistory.vue'

const confirmRequire = vi.fn()

vi.mock('primevue/usetoast', () => ({
  useToast: () => ({ add: vi.fn() }),
}))

vi.mock('primevue/useconfirm', () => ({
  useConfirm: () => ({ require: confirmRequire }),
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
    createVersionSnapshot: vi.fn(),
    restoreVersion: vi.fn(),
  },
}))

beforeAll(() => {
  vi.stubGlobal('IntersectionObserver', class {
    observe() {}
    disconnect() {}
  })
})

describe('AppVersionHistory', () => {
  it('describes restore history as source snapshots, not deployed versions', async () => {
    const wrapper = mount(AppVersionHistory, {
      props: { appId: 'app-id', fullPage: true },
      global: {
        plugins: [PrimeVue],
        directives: { tooltip: Tooltip },
      },
    })
    await flushPromises()

    expect(wrapper.text()).toContain('Source snapshots')
    expect(wrapper.text()).toContain('Snapshot 3')

    const restoreButton = wrapper.findAll('button').find((button) =>
      button.attributes('class')?.includes('p-button-warn'),
    )
    if (!restoreButton) throw new Error('Restore source snapshot button not found')
    await restoreButton.trigger('click')

    expect(confirmRequire).toHaveBeenCalledWith(expect.objectContaining({
      message: expect.stringContaining('does not deploy the source or roll back the running application'),
      acceptLabel: 'Restore source',
    }))
  })
})
