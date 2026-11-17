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
