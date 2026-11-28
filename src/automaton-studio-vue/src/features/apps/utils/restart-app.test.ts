import { describe, expect, it, vi } from 'vitest'
import { restartApp } from './restart-app'
import { appsService } from '../services/apps.service'

describe('restartApp', () => {
  it('says Restarted once the app is back', async () => {
    vi.spyOn(appsService, 'restart').mockResolvedValue({ restarted: 1, confirmed: true })
    const toast = { add: vi.fn() }
    await restartApp('a1', toast as never)
    expect(toast.add).toHaveBeenCalledWith(expect.objectContaining({ severity: 'success', summary: 'Restarted' }))
  })

  it('says why when the restart fails', async () => {
    vi.spyOn(appsService, 'restart').mockRejectedValue(new Error('boom'))
    const toast = { add: vi.fn() }
    await restartApp('a1', toast as never)
    expect(toast.add).toHaveBeenCalledWith(expect.objectContaining({ severity: 'error', summary: 'Could not restart' }))
  })
})
