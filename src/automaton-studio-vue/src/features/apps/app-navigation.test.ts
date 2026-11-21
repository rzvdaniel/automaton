import { describe, expect, it } from 'vitest'
import {
  appTabs,
  DEFAULT_APP_VIEW,
  resolveAppView,
  withoutAuthoringQuery,
} from './app-navigation'

describe('app page navigation', () => {
  it('opens on Overview; a read-only person sees Overview and Activity', () => {
    expect(DEFAULT_APP_VIEW).toBe('overview')
    expect(appTabs().map((tab) => tab.label)).toEqual(['Overview', 'Activity'])
  })

  it('shows each tab only where it applies', () => {
    const labels = (options: Parameters<typeof appTabs>[0]) => appTabs(options).map((tab) => tab.label)
    expect(labels({ canManage: true, isPublishable: true, hasSecrets: true, hasActions: true, isAdmin: true }))
      .toEqual(['Overview', 'Activity', 'Actions', 'Source', 'Secrets', 'Domain', 'Store listing', 'Settings', 'Admin'])
    expect(labels({ canManage: true, isPublishable: true })).toEqual(['Overview', 'Activity', 'Source', 'Domain', 'Store listing', 'Settings'])
    expect(labels({ canManage: true })).toEqual(['Overview', 'Activity', 'Source', 'Settings'])
    expect(labels({ isAdmin: true })).toEqual(['Overview', 'Activity', 'Source', 'Admin'])
  })

  it('badges the Admin tab with the waiting permission requests, only when there are some', () => {
    const admin = (waitingPermissions: number) => appTabs({ isAdmin: true, waitingPermissions }).find((tab) => tab.key === 'admin')
    expect(admin(2)?.badge).toBe(2)
    expect(admin(0)?.badge).toBeUndefined()
    expect(appTabs({ canManage: true, waitingPermissions: 1 }).some((tab) => tab.badge)).toBe(false)
  })

  it('keeps every earlier ?view= link working', () => {
    for (const view of ['logs', 'deployments']) expect(resolveAppView(view)).toBe('activity')
    for (const view of ['runtime', 'targets', 'contract']) expect(resolveAppView(view)).toBe('overview')
    expect(resolveAppView('more')).toBe('settings')
    for (const view of ['actions', 'source', 'secrets', 'domain', 'store', 'settings', 'admin']) expect(resolveAppView(view)).toBe(view)
  })

  it('falls back to Overview for removed authoring views and unknown values', () => {
    expect(resolveAppView('build')).toBe('overview')
    expect(resolveAppView('files')).toBe('overview')
    expect(resolveAppView('snapshots')).toBe('overview')
    expect(resolveAppView('publishing')).toBe('overview')
    expect(resolveAppView('unknown')).toBe('overview')
    expect(resolveAppView(undefined)).toBe('overview')
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
