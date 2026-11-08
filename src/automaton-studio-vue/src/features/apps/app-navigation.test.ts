import { describe, expect, it } from 'vitest'
import {
  APP_SUB_VIEWS,
  APP_TABS,
  DEFAULT_APP_VIEW,
  resolveAppView,
  resolveAppViewFromQuery,
  tabForView,
  withoutAuthoringQuery,
} from './app-navigation'

describe('app page navigation', () => {
  it('opens on Overview and has four tabs', () => {
    expect(DEFAULT_APP_VIEW).toBe('overview')
    expect(APP_TABS.map((tab) => tab.label)).toEqual(['Overview', 'Settings & secrets', 'Activity', 'Advanced'])
  })

  it('keeps every earlier destination reachable inside a tab', () => {
    const views = new Set<string>(APP_TABS.map((tab) => tab.view))
    for (const subViews of Object.values(APP_SUB_VIEWS)) subViews?.forEach((item) => views.add(item.key))
    views.add('publishing')
    for (const key of ['overview', 'deployments', 'runtime', 'logs', 'actions', 'publishing', 'settings']) {
      expect(views.has(key)).toBe(true)
    }
    expect(tabForView('logs')).toBe('activity')
    expect(tabForView('deployments')).toBe('activity')
    expect(tabForView('runtime')).toBe('advanced')
    expect(tabForView('publishing')).toBe('overview')
  })

  it('falls back to Overview for removed authoring views and unknown values', () => {
    expect(resolveAppView('runtime')).toBe('runtime')
    expect(resolveAppView('build')).toBe('overview')
    expect(resolveAppView('files')).toBe('overview')
    expect(resolveAppView('snapshots')).toBe('overview')
    expect(resolveAppView('unknown')).toBe('overview')
  })

  it('maps old settings deep links to their new place', () => {
    expect(resolveAppViewFromQuery({ view: 'settings', settings: 'targets' })).toBe('targets')
    expect(resolveAppViewFromQuery({ view: 'settings', settings: 'permissions' })).toBe('settings')
    expect(resolveAppViewFromQuery({ view: 'publishing' })).toBe('publishing')
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
