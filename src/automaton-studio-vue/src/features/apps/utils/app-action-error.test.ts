import { describe, expect, it } from 'vitest'
import { APP_ACTION_TIMEOUT_MS, appActionErrorMessage, isConnectionDropped } from './app-action-error'

describe('appActionErrorMessage', () => {
  it('shows the API message with the app reason', () => {
    const failure = { response: { data: { message: 'Invalid setting: DISK_FREE_WARN_PERCENT must be a whole number from 1 to 99' } } }
    expect(appActionErrorMessage(failure, 'fallback')).toContain('Invalid setting')
  })

  it('explains a client timeout', () => {
    expect(appActionErrorMessage({ code: 'ECONNABORTED' }, 'fallback')).toContain('stopped waiting')
  })

  it('falls back otherwise', () => {
    expect(appActionErrorMessage(new Error('x'), 'fallback')).toBe('fallback')
  })

  it('waits longer than the API does (150 s)', () => {
    expect(APP_ACTION_TIMEOUT_MS).toBeGreaterThan(150_000)
  })

  it('tells a dropped connection from an error the server answered', () => {
    expect(isConnectionDropped({ code: 'ERR_NETWORK' })).toBe(true)
    expect(isConnectionDropped({ code: 'ERR_NETWORK', response: { status: 502 } })).toBe(false)
    expect(isConnectionDropped({ code: 'ECONNABORTED' })).toBe(false)
    expect(isConnectionDropped(null)).toBe(false)
  })
})
