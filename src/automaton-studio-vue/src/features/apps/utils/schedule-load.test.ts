import { describe, expect, it } from 'vitest'
import { isCurrentScheduleLoad } from './schedule-load'

describe('Action scheduling app navigation', () => {
  it('rejects a late App A response after navigation to App B', () => {
    expect(isCurrentScheduleLoad('app-a', 'app-b', 1, 2)).toBe(false)
  })

  it('accepts only the latest response for the current app', () => {
    expect(isCurrentScheduleLoad('app-b', 'app-b', 2, 2)).toBe(true)
    expect(isCurrentScheduleLoad('app-b', 'app-b', 1, 2)).toBe(false)
  })
})
