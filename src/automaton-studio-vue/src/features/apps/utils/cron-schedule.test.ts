import { describe, expect, it } from 'vitest'
import { buildCron, formatNextRuns, isValidCron, nextRuns, timingFromCron, timingFromInterval, timingProblem } from './cron-schedule'

const from = new Date('2026-11-22T11:07:00Z')
describe('cron-schedule', () => {
  it('builds cron from the friendly choices', () => {
    const t = timingFromCron('*/15 * * * *')
    expect(buildCron({ ...t, mode: 'every', count: 15, unit: 'minutes' })).toBe('*/15 * * * *')
    expect(buildCron({ ...t, mode: 'every', count: 6, unit: 'hours' })).toBe('0 */6 * * *')
    expect(buildCron({ ...t, mode: 'daily', time: '02:05' })).toBe('5 2 * * *')
    expect(buildCron({ ...t, mode: 'cron', cron: ' 0 2 * * 1-5 ' })).toBe('0 2 * * 1-5')
  })

  it('reads stored cron back into a friendly choice, else cron', () => {
    expect(timingFromCron('*/15 * * * *')).toMatchObject({ mode: 'every', count: 15, unit: 'minutes' })
    expect(timingFromCron('0 */6 * * *')).toMatchObject({ mode: 'every', count: 6, unit: 'hours' })
    expect(timingFromCron('0 2 * * *')).toMatchObject({ mode: 'daily', time: '02:00' })
    expect(timingFromCron('0 2 * * 1-5')).toMatchObject({ mode: 'cron', cron: '0 2 * * 1-5' })
    expect(timingFromCron('*/90 * * * *')).toMatchObject({ mode: 'cron' })
  })

  it('opens an interval schedule as every N', () => {
    expect(timingFromInterval(900)).toMatchObject({ mode: 'every', count: 15, unit: 'minutes' })
    expect(timingFromInterval(7200)).toMatchObject({ mode: 'every', count: 2, unit: 'hours' })
  })

  it('validates', () => {
    expect(isValidCron('0 2 * * 1-5')).toBe(true)
    expect(isValidCron('0 2 * *')).toBe(false)
    expect(isValidCron('61 2 * * *')).toBe(false)
    expect(timingProblem({ ...timingFromCron('*/15 * * * *'), count: 0 })).toContain('whole number')
    expect(timingProblem(timingFromCron('*/15 * * * *'))).toBeNull()
  })

  it('lists the next runs in UTC and in a zone', () => {
    expect(nextRuns('*/15 * * * *', 'UTC', 3, from).map((d) => d.toISOString())).toEqual(['2026-11-22T11:15:00.000Z', '2026-11-22T11:30:00.000Z', '2026-11-22T11:45:00.000Z'])
    expect(nextRuns('0 2 * * *', 'Europe/Bucharest', 1, from)[0]!.toISOString()).toBe('2026-11-23T00:00:00.000Z')
    expect(nextRuns('0 2 * * 1-5', 'UTC', 2, from).map((d) => d.toISOString())).toEqual(['2026-11-23T02:00:00.000Z', '2026-11-24T02:00:00.000Z'])
    expect(nextRuns('nope', 'UTC')).toEqual([])
  })

  it('finds runs in zones that are not a whole hour from UTC', () => {
    expect(nextRuns('0 2 * * *', 'Asia/Kolkata', 1, from)[0]!.toISOString()).toBe('2026-11-22T20:30:00.000Z')
    expect(nextRuns('*/15 * * * *', 'Asia/Kolkata', 3, from).map((d) => d.toISOString())).toEqual(['2026-11-22T11:15:00.000Z', '2026-11-22T11:30:00.000Z', '2026-11-22T11:45:00.000Z'])
    expect(nextRuns('45 5 * * *', 'Asia/Kathmandu', 1, from)[0]!.toISOString()).toBe('2026-11-22T00:00:00.000Z'.replace('22T00', '23T00'))
  })

  it('follows a clock change', () => {
    // Europe/Bucharest leaves summer time on 2026-10-25: 02:00 local is 23:00Z the day before, then 00:00Z after.
    const runs = nextRuns('0 2 * * *', 'Europe/Bucharest', 3, new Date('2026-10-23T12:00:00Z')).map((d) => d.toISOString())
    expect(runs).toEqual(['2026-10-23T23:00:00.000Z', '2026-10-24T23:00:00.000Z', '2026-10-26T00:00:00.000Z'])
  })

  it('formats the line', () => {
    expect(formatNextRuns(nextRuns('*/15 * * * *', 'UTC', 3, from), 'UTC', from)).toBe('11:15, 11:30 and 11:45 UTC')
  })
})
