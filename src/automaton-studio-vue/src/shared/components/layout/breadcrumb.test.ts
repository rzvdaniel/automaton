import { describe, expect, it } from 'vitest'
import { crumbsForRoute } from './breadcrumb'

describe('crumbsForRoute', () => {
  it('has no trail on top-level pages', () => {
    for (const name of ['home', 'apps', 'apps-catalog', 'people', 'server', 'admin-backups', 'settings']) {
      expect(crumbsForRoute(name)).toEqual([])
    }
  })

  it('shows Apps > app name on an app page', () => {
    expect(crumbsForRoute('app-editor', { params: { id: 'a1' }, query: {}, appName: 'Server Keeper' }))
      .toEqual([{ label: 'Apps', to: '/apps' }, { label: 'Server Keeper' }])
  })

  it('shows Apps > app > Settings with working links', () => {
    expect(crumbsForRoute('app-editor', { params: { id: 'a1' }, query: { view: 'settings' }, appName: 'Server Keeper' }))
      .toEqual([{ label: 'Apps', to: '/apps' }, { label: 'Server Keeper', to: '/apps/a1' }, { label: 'Settings' }])
  })

  it('ignores an unknown view', () => {
    expect(crumbsForRoute('app-editor', { params: { id: 'a1' }, query: { view: 'nope' }, appName: 'X' })).toHaveLength(2)
  })

  it('links admin pages back to Admin', () => {
    expect(crumbsForRoute('admin-logs', { meta: 'Audit Events' })).toEqual([{ label: 'Admin', to: '/admin/apps' }, { label: 'Audit Events' }])
  })
})
