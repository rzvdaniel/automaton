// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import ToastService from 'primevue/toastservice'
import AppActions from './AppActions.vue'
import { actionSchedulesService } from '../services/action-schedules.service'
import { appsService } from '../services/apps.service'

vi.mock('../services/apps.service', () => ({ appsService: { runAction: vi.fn(), getActionExecution: vi.fn() } }))
vi.mock('../services/action-schedules.service', () => ({ actionSchedulesService: { setEnabled: vi.fn() } }))

const actions = [{ key: 'upload', name: 'upload', timeoutSeconds: 60, schedulable: true, allowedExecutionScopes: [], minimumIntervalSeconds: 60 }]
const schedules = [{
  id: 's1', applicationId: 'a', actionKey: 'upload', name: 'upload', executionScope: 'App', scheduleType: 'Interval' as const,
  intervalSeconds: 86400, timeZone: 'UTC', enabled: true, nextRunAtUtc: new Date(Date.now() + 3 * 3600_000).toISOString(),
  concurrencyPolicy: 'Allow' as const, misfirePolicy: 'Skip' as const,
}]
const executions = [{ id: 'r1', appId: 'a', moduleKey: 'upload', status: 'Succeeded', createdAtUtc: new Date(Date.now() - 3600_000).toISOString() }]

function mountIt(extra: Record<string, unknown> = {}) {
  return mount(AppActions, {
    props: { appId: 'a', paused: false, canUpdate: true, actions, schedules, executions, ...extra },
    global: { plugins: [PrimeVue, ToastService] },
  })
}

describe('AppActions', () => {
  beforeEach(() => vi.clearAllMocks())

  it('shows schedule, next run and last result', () => {
    const text = mountIt().text()
    expect(text).toContain('every day')
    expect(text).toContain('next run in 3 hours')
    expect(text).toContain('succeeded')
  })

  it('runs now, waits for the result and tells the parent', async () => {
    vi.mocked(appsService.runAction).mockResolvedValue({ id: 'r2', appId: 'a', moduleKey: 'upload', status: 'Succeeded', createdAtUtc: new Date().toISOString() })
    const wrapper = mountIt()
    await wrapper.get('[data-testid="run-now"]').trigger('click')
    await flushPromises()
    expect(appsService.runAction).toHaveBeenCalledWith('a', 'upload', {})
    expect(wrapper.emitted('changed')).toBeTruthy()
  })

  it('switches the schedule off', async () => {
    vi.mocked(actionSchedulesService.setEnabled).mockResolvedValue({} as never)
    const wrapper = mountIt()
    await wrapper.get('[data-testid="schedule-switch"] input').setValue(false)
    await flushPromises()
    expect(actionSchedulesService.setEnabled).toHaveBeenCalledWith('s1', false)
    expect(wrapper.emitted('changed')).toBeTruthy()
  })

  it('shows no controls to a viewer', () => {
    const wrapper = mountIt({ canUpdate: false })
    expect(wrapper.find('[data-testid="run-now"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="schedule-switch"]').exists()).toBe(false)
  })

  it('shows the switch off and disables Run now when paused, with a hint', () => {
    const wrapper = mountIt({ paused: true })
    expect(wrapper.get('[data-testid="run-now"]').attributes('disabled')).toBeDefined()
    expect((wrapper.get('[data-testid="schedule-switch"] input').element as HTMLInputElement).checked).toBe(false)
    expect(wrapper.find('[data-testid="actions-paused-hint"]').exists()).toBe(true)
    expect(wrapper.text()).not.toContain('next run')
  })

  it('lists every schedule of one action, each switch changing only its own', async () => {
    vi.mocked(actionSchedulesService.setEnabled).mockResolvedValue({} as never)
    const many = ['Stall check', 'Morning report', 'Lessons', 'Pick-up'].map((name, i) => ({ ...schedules[0]!, id: `s${i}`, name }))
    const wrapper = mountIt({ schedules: many })
    expect(wrapper.findAll('[data-testid="schedule-row"]')).toHaveLength(4)
    expect(wrapper.text()).toContain('Morning report')
    await wrapper.findAll('[data-testid="schedule-switch"] input')[1]!.setValue(false)
    await flushPromises()
    expect(actionSchedulesService.setEnabled).toHaveBeenCalledTimes(1)
    expect(actionSchedulesService.setEnabled).toHaveBeenCalledWith('s1', false)
  })
})
