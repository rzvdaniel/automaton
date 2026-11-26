// @vitest-environment jsdom
import { beforeEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import router from './index'
import { useAuthStore } from '@/features/auth/stores/auth.store'

describe('studio router', () => {
  it('makes Home the start page', () => {
    const resolved = router.resolve('/')
    expect(resolved.matched.some((record) => record.redirect)).toBe(true)
    expect(router.resolve({ name: 'home' }).path).toBe('/home')
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

  it.each([
    ['/catalog', 'apps-catalog'],
    ['/backups', 'admin-backups'],
  ])('sends the menu name %s to its page', (path, name) => {
    const matched = router.resolve(path).matched
    expect(matched[matched.length - 1]?.redirect).toEqual({ name })
  })

  it.each(['organizations', 'admin-users'])('sends the %s list to the People page', (name) => {
    expect(router.getRoutes().find((route) => route.name === name)?.redirect).toEqual({ name: 'people' })
  })

  it('sends the old admin app page to the app page Admin tab', () => {
    const matched = router.resolve('/admin/apps/a1').matched
    const redirect = matched[matched.length - 1]?.redirect as (to: { params: Record<string, string> }) => unknown
    expect(redirect({ params: { appId: 'a1' } })).toEqual({ path: '/apps/a1', query: { view: 'admin' } })
  })

  it.each(['apikeys', 'downloads', 'admin-installation'])('sends the old %s page to Settings', (name) => {
    const redirect = router.getRoutes().find((route) => route.name === name)?.redirect as { path: string; hash: string }
    expect(redirect.path).toBe('/settings')
    expect(['#keys', '#connect', '#email']).toContain(redirect.hash)
  })

  describe('CLI approval page', () => {
    const approval = '/cli/authorize?code=abc&state=x%20y'

    beforeEach(async () => {
      setActivePinia(createPinia())
      await router.replace('/login')
    })

    it('keeps a signed-in user on the page with its query', async () => {
      const auth = useAuthStore()
      auth.accessToken = 'token'
      auth.user = { id: 'u1', email: 'a@b.c', name: 'A' } as typeof auth.user
      auth.isInitialized = true
      await router.push(approval)
      expect(router.currentRoute.value.name).toBe('cli-authorize')
      expect(router.currentRoute.value.fullPath).toBe(approval)
    })

    it('sends a signed-out user to login and back to the page', async () => {
      const auth = useAuthStore()
      auth.isInitialized = true
      await router.push(approval)
      const current = router.currentRoute.value
      expect(current.path).toBe('/login')
      expect(current.query.redirect).toBe(approval)
    })
  })

  describe('Claude connector consent page', () => {
    const consent = '/mcp/authorize?client_id=abc&state=x%20y'

    beforeEach(async () => {
      setActivePinia(createPinia())
      await router.replace('/login')
    })

    it('keeps a signed-in user on the page with its query', async () => {
      const auth = useAuthStore()
      auth.accessToken = 'token'
      auth.user = { id: 'u1', email: 'a@b.c', name: 'A' } as typeof auth.user
      auth.isInitialized = true
      await router.push(consent)
      expect(router.currentRoute.value.name).toBe('mcp-authorize')
      expect(router.currentRoute.value.fullPath).toBe(consent)
    })

    it('sends a signed-out user to login and back to the page', async () => {
      const auth = useAuthStore()
      auth.isInitialized = true
      await router.push(consent)
      const current = router.currentRoute.value
      expect(current.path).toBe('/login')
      expect(current.query.redirect).toBe(consent)
    })

    it('still sends a signed-in user away from the login page', async () => {
      const auth = useAuthStore()
      auth.accessToken = 'token'
      auth.user = { id: 'u1', email: 'a@b.c', name: 'A' } as typeof auth.user
      auth.isInitialized = true
      await router.push('/home')
      await router.push('/login')
      expect(router.currentRoute.value.path).not.toBe('/login')
    })
  })
})
