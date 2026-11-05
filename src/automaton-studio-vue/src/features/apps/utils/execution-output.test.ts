import { describe, expect, it } from 'vitest'
import { composeExecutionOutput } from './execution-output'

describe('composeExecutionOutput', () => {
  it('shows the result when the run has no log output', () => {
    const text = composeExecutionOutput({ available: true, content: '' }, { available: true, content: '{"uploaded":3}' })
    expect(text).toBe('Result\n{\n  "uploaded": 3\n}')
  })

  it('shows logs and result together', () => {
    const text = composeExecutionOutput({ available: true, content: 'started\n' }, { available: true, content: 'ok' })
    expect(text).toBe('Logs\nstarted\n\nResult\nok')
  })

  it('shows only the logs when there is no result', () => {
    expect(composeExecutionOutput({ available: true, content: 'line' }, { available: false, message: 'gone' })).toBe('line')
  })

  it('explains why there is no output', () => {
    expect(composeExecutionOutput({ available: false, message: 'Runner is unavailable.' }, null)).toBe('Runner is unavailable.')
    expect(composeExecutionOutput({ available: true, content: '' }, { available: true, content: '' })).toBe('This run produced no log output and no result.')
  })
})
