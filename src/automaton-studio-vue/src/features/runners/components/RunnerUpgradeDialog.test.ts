// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import PrimeVue from 'primevue/config'
import RunnerUpgradeDialog from './RunnerUpgradeDialog.vue'
import { runnersService } from '../services/runners.service'

vi.mock('../services/runners.service', () => ({
  runnersService: { startUpgrade: vi.fn(), getUpgradeStatus: vi.fn(), prepareUpgrade: vi.fn() },
}))

const mountDialog = (props = {}) =>
  mount(RunnerUpgradeDialog, {
    attachTo: document.body,
    props: { visible: true, runnerId: 'r1', runnerName: 'Machine', online: true, upgradeAvailable: true, ...props },
    global: { plugins: [PrimeVue] },
  })
const status = (state: string, message = 'm') => ({ runnerId: 'r1', state, message })

describe('RunnerUpgradeDialog', () => {
  beforeEach(() => { vi.useFakeTimers(); vi.mocked(runnersService.startUpgrade).mockReset(); vi.mocked(runnersService.getUpgradeStatus).mockReset() })
  afterEach(() => { vi.useRealTimers(); document.body.innerHTML = '' })

  it('leads with Upgrade now and reports the Runner reconnected', async () => {
    vi.mocked(runnersService.startUpgrade).mockResolvedValue(status('InProgress') as never)
    vi.mocked(runnersService.getUpgradeStatus).mockResolvedValue(status('Reconnected') as never)
    mountDialog()
    await flushPromises()
    document.querySelector<HTMLButtonElement>('[data-testid="runner-upgrade-now"]')!.click()
    await flushPromises()
    expect(document.querySelector('[data-testid="runner-upgrade-progress"]')).not.toBeNull()
    await vi.advanceTimersByTimeAsync(4000)
    expect(document.querySelector('[data-testid="runner-upgrade-result"]')?.textContent).toContain('reconnected on the new image')
  })

  it('reports a rollback', async () => {
    vi.mocked(runnersService.startUpgrade).mockResolvedValue(status('InProgress') as never)
    vi.mocked(runnersService.getUpgradeStatus).mockResolvedValue(status('RolledBack', 'restored') as never)
    mountDialog()
    await flushPromises()
    document.querySelector<HTMLButtonElement>('[data-testid="runner-upgrade-now"]')!.click()
    await flushPromises()
    await vi.advanceTimersByTimeAsync(4000)
    expect(document.querySelector('[data-testid="runner-upgrade-result"]')?.textContent).toContain('rolled back')
  })

  it('does not offer the one-click upgrade for an offline Runner', async () => {
    mountDialog({ online: false })
    await flushPromises()
    expect(document.querySelector<HTMLButtonElement>('[data-testid="runner-upgrade-now"]')!.disabled).toBe(true)
    expect(document.body.textContent).toContain('Use the manual command')
  })
})
