import { describe, expect, it } from 'vitest'
import { localInputToUtc, utcToLocalInput } from './schedule-time'

describe.each([
  ['UTC', '2026-01-15T08:30:00.000Z', 0],
  ['Europe/Madrid summer DST', '2026-07-26T08:30:00.000Z', -120],
  ['Asia/Kolkata positive offset', '2026-01-15T08:30:00.000Z', -330],
  ['America/New_York summer DST', '2026-07-26T08:30:00.000Z', 240],
  ['America/New_York winter', '2026-01-15T08:30:00.000Z', 300],
])('schedule time conversion in %s', (_timeZone, instant, offsetMinutes) => {
  it('preserves the instant when opened and saved unchanged', () => {
    expect(localInputToUtc(utcToLocalInput(instant, offsetMinutes), offsetMinutes)).toBe(instant)
  })
})
