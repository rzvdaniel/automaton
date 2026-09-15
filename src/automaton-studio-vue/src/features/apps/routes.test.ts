// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import { appsRoutes } from './routes'

describe('app routes', () => {
  it('redirects the removed browser creation route to the app list', async () => {
    const router = createRouter({ history: createMemoryHistory(), routes: appsRoutes })

    await router.push('/apps/new?prompt=build-an-app')
    await router.isReady()

    expect(router.currentRoute.value.name).toBe('apps')
    expect(router.currentRoute.value.path).toBe('/apps')
  })

  it('redirects old browser remix links to the app list', async () => {
    const router = createRouter({ history: createMemoryHistory(), routes: appsRoutes })

    await router.push('/apps/remix/template-id')
    await router.isReady()

    expect(router.currentRoute.value.name).toBe('apps')
    expect(router.currentRoute.value.path).toBe('/apps')
  })
})
