import { describe, expect, it } from 'vitest'
import {
  APP_TABS,
  DEFAULT_APP_VIEW,
  opensAdvanced,
  resolveAppView,
  resolveAppViewFromQuery,
  withoutAuthoringQuery,
} from './app-navigation'

describe('app page navigation', () => {
  it('opens on Overview and has three plain tabs', () => {
    expect(DEFAULT_APP_VIEW).toBe('overview')
    expect(APP_TABS.map((tab) => tab.label)).toEqual(['Overview', 'Activity', 'Settings'])
  })

  it('keeps every earlier ?view= link working', () => {
    for (const view of ['logs', 'deployments', 'actions']) expect(resolveAppView(view)).toBe('activity')
    for (const view of ['runtime', 'targets', 'contract', 'more']) expect(resolveAppView(view)).toBe('settings')
    expect(resolveAppView('settings')).toBe('settings')
  })

  it('falls back to Overview for removed authoring views and unknown values', () => {
    expect(resolveAppView('build')).toBe('overview')
    expect(resolveAppView('files')).toBe('overview')
    expect(resolveAppView('snapshots')).toBe('overview')
    expect(resolveAppView('publishing')).toBe('overview')
    expect(resolveAppView('unknown')).toBe('overview')
    expect(resolveAppView(undefined)).toBe('overview')
  })

  it('opens Advanced for links that pointed at what Advanced now holds', () => {
    expect(resolveAppViewFromQuery({ view: 'settings', settings: 'targets' })).toBe('settings')
    expect(opensAdvanced({ view: 'settings', settings: 'targets' })).toBe(true)
    expect(opensAdvanced({ view: 'more' })).toBe(true)
    expect(opensAdvanced({ view: 'runtime' })).toBe(true)
    expect(opensAdvanced({ view: 'settings' })).toBe(false)
    expect(opensAdvanced({ view: 'logs' })).toBe(false)
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

  it('removes legacy Source snapshots deep links without dropping the selected target', () => {
    expect(withoutAuthoringQuery({ view: 'snapshots', target: 'staging' })).toEqual({
      target: 'staging',
    })
  })
})
