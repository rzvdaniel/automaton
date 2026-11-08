// @vitest-environment jsdom
import { describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import ToastService from 'primevue/toastservice'
import AppActivity from './AppActivity.vue'
import { actionSchedulesService } from '../services/action-schedules.service'

vi.mock('@/shared/services/api/application-events.service', () => ({
  applicationEventsService: { getEvents: vi.fn().mockResolvedValue({ items: [] }) },
}))
vi.mock('../services/apps.service', () => ({ appsService: { listSecretHistory: vi.fn().mockResolvedValue([]) } }))
vi.mock('../services/action-schedules.service', () => ({
  actionSchedulesService: { getLogs: vi.fn(), getResult: vi.fn() },
}))

const runs = [
  { id: 'r1', appId: 'a', moduleKey: 'nightly', status: 'Succeeded', createdAtUtc: '2026-11-08T03:00:00Z' },
  { id: 'r2', appId: 'a', moduleKey: 'sync', status: 'Failed', failureSummary: 'Boom', createdAtUtc: '2026-11-08T02:00:00Z' },
]

describe('AppActivity run output', () => {
  it('opens a run inline with a loading state, caches the output and keeps one row open', async () => {
    vi.mocked(actionSchedulesService.getLogs).mockResolvedValue([] as never)
    vi.mocked(actionSchedulesService.getResult).mockResolvedValue(null as never)
    const wrapper = mount(AppActivity, {
      props: { appId: 'a', executions: runs, liveOutputAvailable: false, liveLogs: [] },
      global: { plugins: [PrimeVue, ToastService] },
    })
    await flushPromises()
    const rows = wrapper.findAll('[data-testid="activity-row"]')
    expect(rows[0]!.attributes('aria-expanded')).toBe('false')

    await rows[0]!.trigger('click')
    expect(wrapper.text()).toContain('Loading output')
    await flushPromises()
    expect(rows[0]!.attributes('aria-expanded')).toBe('true')
    expect(wrapper.find('[data-testid="run-output"]').exists()).toBe(true)

    await rows[1]!.trigger('keydown.enter')
    await flushPromises()
    expect(rows[0]!.attributes('aria-expanded')).toBe('false')
    expect(wrapper.findAll('[data-testid="run-output-box"]')).toHaveLength(1)

    await rows[0]!.trigger('click')
    await flushPromises()
    expect(actionSchedulesService.getLogs).toHaveBeenCalledTimes(2)
  })
})
