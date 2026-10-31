export interface StoreConfig {
  apiBaseUrl: string
  publicAppsBaseUrl: string
  studioUrl: string
  storeTitle: string
  logoUrl: string
}

export interface StoreConfigSources {
  runtime?: AutomatonStoreRuntimeConfig
  env?: Partial<Record<string, string | undefined>>
  origin: string
}

const defaultTitle = 'Automaton App Store'

function trimTrailingSlash(value: string) {
  return value.replace(/\/+$/, '')
}

function ensureTrailingSlash(value: string) {
  return `${trimTrailingSlash(value)}/`
}

function pick(...values: Array<string | undefined>) {
  return values.map((value) => value?.trim()).find((value) => value) ?? ''
}

// Installed servers serve the store at store.<domain>; sibling hosts follow the installer layout.
function siblingHost(origin: string, name: string) {
  try {
    const url = new URL(origin)
    if (!url.hostname.startsWith('store.')) return ''
    url.hostname = `${name}.${url.hostname.slice('store.'.length)}`
    return url.origin
  } catch {
    return ''
  }
}

export function resolveStoreConfig({ runtime = {}, env = {}, origin }: StoreConfigSources): StoreConfig {
  const apiBaseUrl = pick(runtime.apiBaseUrl, env.VITE_API_BASE_URL, siblingHost(origin, 'api')) || origin
  // Without an apps host (local or custom store host), the API serves published apps by path.
  const publicAppsBaseUrl =
    pick(runtime.publicAppsBaseUrl, env.VITE_PUBLIC_APPS_BASE_URL, siblingHost(origin, 'apps')) || apiBaseUrl
  const studioUrl = pick(runtime.studioUrl, env.VITE_STUDIO_URL, siblingHost(origin, 'studio'))

  return {
    apiBaseUrl: ensureTrailingSlash(apiBaseUrl),
    publicAppsBaseUrl: trimTrailingSlash(publicAppsBaseUrl),
    studioUrl: studioUrl ? ensureTrailingSlash(studioUrl) : '',
    storeTitle: pick(runtime.storeTitle, env.VITE_STORE_TITLE) || defaultTitle,
    logoUrl: pick(runtime.logoUrl, env.VITE_STORE_LOGO_URL),
  }
}

export function buildPublishedAppUrl(publicAppsBaseUrl: string, slug: string | null | undefined, origin: string) {
  const normalizedSlug = slug?.trim()
  if (!normalizedSlug || !publicAppsBaseUrl) return ''

  const encodedSlug = encodeURIComponent(normalizedSlug)
  const base = new URL(publicAppsBaseUrl, origin)
  const host = base.hostname.toLowerCase()
  const isLocal = host === 'localhost' || host === '127.0.0.1' || host === '[::1]'
  if (!isLocal && host.startsWith('apps.')) {
    base.hostname = `${encodedSlug}.apps.${host.slice('apps.'.length)}`
    base.pathname = '/'
    base.search = ''
    base.hash = ''
    return base.toString()
  }

  const namespaced = (base.pathname && base.pathname !== '/') || !host.startsWith('apps.')
  return `${trimTrailingSlash(publicAppsBaseUrl)}${namespaced ? '/apps' : ''}/${encodedSlug}/`
}
