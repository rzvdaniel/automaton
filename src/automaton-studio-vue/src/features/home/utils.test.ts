import { describe, expect, it } from 'vitest'
import type { AppListItem } from '@/features/apps/types/apps.types'
import { appFacts, moved } from './utils'

const app = (extra: Partial<AppListItem>): AppListItem => ({
  id: 'a', name: 'Shop', summary: '', description: '', tags: [], isPublished: false, updatedAtUtc: '', canDelete: false,
  runtime: { genericRuntimeDeployed: true }, ...extra,
})

describe('appFacts', () => {
  const now = new Date('2026-11-17T12:00:00Z')

  it('shows last deploy and Runner for a web app', () => {
    const facts = appFacts(app({
      runner: { id: 'r', name: 'home-box', purpose: 'x', connectivityStatus: 'Online' },
      liveEnvironment: { name: 'Live', status: 'Running', isHostedRunner: false, runnerOnline: true, genericRuntimeDeployed: true, routePath: '', lastSuccessfulDeploymentAtUtc: '2026-11-17T09:00:00Z' },
    }), now)
    expect(facts).toEqual([{ label: 'Last deploy', value: '3h ago' }, { label: 'Runner', value: 'home-box' }])
  })

  it('adds the last Action run and says never when nothing was deployed', () => {
    const facts = appFacts(app({ lastActionRun: { moduleKey: 'nightly', status: 'Succeeded', createdAtUtc: '2026-11-17T11:30:00Z' } }), now)
    expect(facts).toEqual([
      { label: 'Last deploy', value: 'never' },
      { label: 'Last Action run', value: 'nightly succeeded 30m ago' },
      { label: 'Runner', value: 'none' },
    ])
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
