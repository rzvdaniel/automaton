import { buildPublishedAppUrl, resolveStoreConfig } from './resolveStoreConfig'

export const publicAppConfig = resolveStoreConfig({
  runtime: globalThis.__AUTOMATON_CONFIG__,
  env: import.meta.env,
  origin: window.location.origin,
})

export function getPublishedAppUrl(slug?: string | null): string {
  return buildPublishedAppUrl(publicAppConfig.publicAppsBaseUrl, slug, window.location.origin)
}
