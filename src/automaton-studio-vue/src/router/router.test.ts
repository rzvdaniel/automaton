// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'
import router from './index'

describe('studio router', () => {
  it('makes Apps the start page', () => {
    const resolved = router.resolve('/')
    expect(resolved.matched.some((record) => record.name === 'apps' || record.redirect)).toBe(true)
    expect(router.resolve({ name: 'apps' }).path).toBe('/apps')
  })

  it('never navigates to a route name that does not exist', () => {
    const sources = import.meta.glob('../**/*.{vue,ts}', { query: '?raw', import: 'default', eager: true }) as Record<string, string>
    const known = new Set(router.getRoutes().map((route) => route.name).filter(Boolean))
    const missing: string[] = []
    for (const [file, text] of Object.entries(sources)) {
      if (file.endsWith('.test.ts')) continue
      for (const match of text.matchAll(/(?:router|\$router)\.(?:push|replace)\(\{\s*name:\s*'([^']+)'/g)) {
        if (!known.has(match[1])) missing.push(`${file}: ${match[1]}`)
      }
    }
    expect(missing).toEqual([])
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
    ['/server', 'server'],
    ['/settings', 'settings'],
    ['/people', 'people'],
    ['/organizations', 'organizations'],
    ['/organizations/abc/members', 'organization-members'],
    ['/admin/users', 'admin-users'],
    ['/admin/apps', 'admin-apps'],
  ])('keeps %s', (path, name) => {
    expect(router.resolve(path).name).toBe(name)
  })

  it.each(['organizations', 'admin-users'])('sends the %s list to the People page', (name) => {
    expect(router.getRoutes().find((route) => route.name === name)?.redirect).toEqual({ name: 'people' })
  })
})
