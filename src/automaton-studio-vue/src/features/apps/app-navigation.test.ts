import { describe, expect, it } from 'vitest'
import {
  APP_NAVIGATION_GROUPS,
  DEFAULT_APP_VIEW,
} from './app-navigation'

describe('app operations navigation', () => {
  it('opens on Overview and puts operational destinations before legacy authoring', () => {
    expect(DEFAULT_APP_VIEW).toBe('overview')
    expect(APP_NAVIGATION_GROUPS.map((group) => group.label)).toEqual([
      'Operate',
      'Manage',
      'Legacy authoring',
    ])
    expect(APP_NAVIGATION_GROUPS[0]?.items.map((item) => item.label)).toEqual([
      'Overview',
      'Deployments',
      'Runtime',
      'Logs',
      'Actions',
    ])
  })

  it('keeps source authoring reachable only in the transitional group', () => {
    const legacyGroup = APP_NAVIGATION_GROUPS.find((group) => group.label === 'Legacy authoring')

    expect(legacyGroup?.description).toBe('Temporary access before Stage 2')
    expect(legacyGroup?.items.map((item) => item.label)).toEqual([
      'AI workspace',
      'Source editor',
    ])
  })
})
