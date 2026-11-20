import { describe, expect, it } from 'vitest'
import type { AppListItem } from '@/features/apps/types/apps.types'
import { footerLine, moved, slotAt } from './utils'

const app = (extra: Partial<AppListItem>): AppListItem => ({
  id: 'a', name: 'Shop', summary: '', description: '', tags: [], isPublished: false, updatedAtUtc: '', canDelete: false,
  runtime: { genericRuntimeDeployed: true }, ...extra,
})

describe('footerLine', () => {
  const now = new Date('2026-11-17T12:00:00Z')
  const live = (at: string | null) => ({ name: 'Live', status: 'Running', isHostedRunner: false, runnerOnline: true, genericRuntimeDeployed: true, routePath: '', lastSuccessfulDeploymentAtUtc: at })

  it('says when it was deployed and when the card was updated', () => {
    const line = footerLine(app({ liveEnvironment: live('2026-11-17T09:00:00Z') }), { updatedAtUtc: '2026-11-17T11:59:00Z', stale: false }, now)
    expect(line).toBe('Deployed 3h ago \u00b7 updated 1m ago')
  })
  it('falls back to the last Action run, then to not deployed, and flags a stale card', () => {
    const run = { moduleKey: 'nightly', status: 'Succeeded', createdAtUtc: '2026-11-17T11:30:00Z' }
    expect(footerLine(app({ lastActionRun: run }), null, now)).toBe('Last run 30m ago')
    expect(footerLine(app({}), { updatedAtUtc: '2026-11-17T11:00:00Z', stale: true }, now)).toBe('Not deployed yet \u00b7 Not up to date, updated 1h ago')
  })
})

describe('slotAt', () => {
  const rects = [{ left: 0, top: 0, width: 100, height: 50 }, { left: 110, top: 0, width: 100, height: 50 }]
  it('finds the slot under the point, skips the dragged one and ignores the gap', () => {
    expect(slotAt(rects, 150, 20, 0)).toBe(1)
    expect(slotAt(rects, 50, 20, 0)).toBe(-1)
    expect(slotAt(rects, 105, 20, -1)).toBe(-1)
  })
})

describe('moved', () => {
  it('moves an item and leaves the original alone', () => {
    const items = ['a', 'b', 'c']
    expect(moved(items, 0, 2)).toEqual(['b', 'c', 'a'])
    expect(items).toEqual(['a', 'b', 'c'])
  })
  it('ignores moves that go nowhere', () => {
    expect(moved(['a', 'b'], 1, 1)).toEqual(['a', 'b'])
    expect(moved(['a', 'b'], 0, 5)).toEqual(['a', 'b'])
  })
})

describe('mosaic', () => {
  it('puts a short card under another short one instead of leaving a gap', async () => {
    const { mosaic } = await import('./utils')
    const { places, height } = mosaic([300, 100, 100, 100], [false, false, false, false], 2, 10)
    expect(places.map((p) => [p.column, p.top])).toEqual([[0, 0], [1, 0], [1, 110], [1, 220]])
    expect(height).toBe(320)
  })
  it('lets a wide card span two columns and ignores wide on one column', async () => {
    const { mosaic } = await import('./utils')
    const wide = mosaic([100, 50, 80], [false, true, false], 3, 10)
    expect(wide.places[1]).toEqual({ column: 1, span: 2, top: 0 })
    expect(wide.places[2]).toEqual({ column: 1, span: 1, top: 60 })
    expect(mosaic([100, 50], [true, true], 1, 10).places.map((p) => p.span)).toEqual([1, 1])
  })
})
