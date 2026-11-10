import { describe, expect, it } from 'vitest'
import { APP_ACTION_TIMEOUT_MS, appActionErrorMessage } from './app-action-error'

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
})
