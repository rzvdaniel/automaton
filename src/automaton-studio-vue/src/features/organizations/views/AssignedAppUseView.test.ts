// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import AssignedAppUseView from './AssignedAppUseView.vue'

const mocks = vi.hoisted(() => ({
  getMyAssignedApps: vi.fn(),
  listActions: vi.fn(),
  createRuntimeLaunch: vi.fn(),
  getRestrictedRuntimeLaunchUrl: vi.fn(() => '/runtime'),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { appId: 'assigned-app' } }),
  useRouter: () => ({ push: vi.fn() }),
}))

vi.mock('../services/organizationsService', () => ({
  organizationsService: { getMyAssignedApps: mocks.getMyAssignedApps },
}))

vi.mock('@/features/apps/services/action-schedules.service', () => ({
  actionSchedulesService: { listActions: mocks.listActions },
}))

vi.mock('@/features/apps/services/apps.service', () => ({
  appsService: {
    createRuntimeLaunch: mocks.createRuntimeLaunch,
    getRestrictedRuntimeLaunchUrl: mocks.getRestrictedRuntimeLaunchUrl,
  },
}))

describe('AssignedAppUseView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.getMyAssignedApps.mockResolvedValue([{
      id: 'assigned-app',
      name: 'Assigned app',
      isPublished: false,
    }])
    mocks.listActions.mockRejectedValue(new Error('Runner unavailable'))
  })

  it('keeps runtime launch available when Action discovery fails', async () => {
    const wrapper = mount(AssignedAppUseView)
    await flushPromises()

    expect(wrapper.text()).toContain('Actions could not be loaded. Runner unavailable')
    expect(wrapper.findAll('button').some(button => button.text().includes('Open app'))).toBe(true)
    expect(wrapper.text()).toContain('Assigned app')
  })
})
