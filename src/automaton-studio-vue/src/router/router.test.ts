// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'
import router from './index'

describe('studio router', () => {
  it('makes Apps the start page', () => {
    const resolved = router.resolve('/')
    expect(resolved.matched.some((record) => record.name === 'apps' || record.redirect)).toBe(true)
    expect(router.resolve({ name: 'apps' }).path).toBe('/apps')
  })

  it.each([
    '/flows',
    '/flows/generate',
    '/steps',
    '/steps/new',
    '/steps/abc',
    '/ai-keys',
    '/ai-keys/add',
    '/ai-keys/abc/edit',
    '/billing',
    '/billing/success',
    '/billing/cancel',
    '/pricing',
    '/projects',
    '/projects/abc/settings',
    '/my-apps',
    '/my-apps/abc',
    '/organizations/abc/projects',
    '/admin/dashboard',
    '/admin/ai-providers',
    '/admin/ai-providers/abc/models',
    '/admin/ai-settings',
    '/admin/ai-guidance',
    '/admin/subscription-plans',
    '/admin/marketplace-apps',
  ])('shows the not-found page for removed route %s', (path) => {
    expect(router.resolve(path).name).toBe('not-found')
  })

  it.each([
    ['/apps', 'apps'],
    ['/apps/catalog', 'apps-catalog'],
    ['/runners', 'runners'],
    ['/settings', 'settings'],
    ['/organizations', 'organizations'],
    ['/organizations/abc/members', 'organization-members'],
    ['/admin/users', 'admin-users'],
    ['/admin/apps', 'admin-apps'],
  ])('keeps %s', (path, name) => {
    expect(router.resolve(path).name).toBe(name)
  })
})
