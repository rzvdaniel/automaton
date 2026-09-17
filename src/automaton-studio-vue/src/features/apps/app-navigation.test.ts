import { describe, expect, it } from 'vitest'
import {
  APP_NAVIGATION_GROUPS,
  DEFAULT_APP_VIEW,
  resolveAppView,
  withoutAuthoringQuery,
} from './app-navigation'

describe('app operations navigation', () => {
  it('opens on Overview and exposes only operational destinations', () => {
    expect(DEFAULT_APP_VIEW).toBe('overview')
    expect(APP_NAVIGATION_GROUPS.map((group) => group.label)).toEqual([
      'Operate',
      'Manage',
    ])
    expect(APP_NAVIGATION_GROUPS[0]?.items.map((item) => item.label)).toEqual([
      'Overview',
      'Deployments',
      'Runtime',
      'Logs',
      'Actions',
    ])
  })

  it('falls back to Overview for removed authoring views and unknown values', () => {
    expect(resolveAppView('runtime')).toBe('runtime')
    expect(resolveAppView('build')).toBe('overview')
    expect(resolveAppView('files')).toBe('overview')
    expect(resolveAppView('unknown')).toBe('overview')
  })

  it('keeps Publishing and Settings as distinct app destinations', () => {
    const items = APP_NAVIGATION_GROUPS.flatMap((group) => group.items)
    const publishing = items.find((item) => item.label === 'Publishing')
    const settings = items.find((item) => item.label === 'Settings')

    expect(publishing?.key).toBe('publishing')
    expect(settings?.key).toBe('settings')
    expect(resolveAppView('publishing')).toBe('publishing')
    expect(resolveAppView('settings')).toBe('settings')
  })

  it('removes prompt and authoring-view deep links without dropping operational context', () => {
    expect(withoutAuthoringQuery({
      prompt: 'build an app',
      mode: 'agent',
      model: 'legacy',
      tab: 'files',
      view: 'build',
      target: 'live',
    })).toEqual({ target: 'live' })
  })
})
