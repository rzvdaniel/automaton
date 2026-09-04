function trimTrailingSlash(value: string) {
  return value.replace(/\/+$/, '')
}

function ensureTrailingSlash(value: string) {
  return `${trimTrailingSlash(value)}/`
}

function joinUrl(base: string, path: string) {
  return `${trimTrailingSlash(base)}/${path.replace(/^\/+/, '')}`
}

function shouldNamespacePublishedApps(baseUrl: string) {
  try {
    const parsed = new URL(baseUrl, window.location.origin)
    if (parsed.pathname && parsed.pathname !== '/') {
      return true
    }

    return !parsed.hostname.startsWith('apps.')
  } catch {
    return true
  }
}

const defaultPublicSiteUrl = import.meta.env.DEV
  ? 'http://localhost:5174/'
  : 'https://automaton.monster/'

const defaultStudioUrl = import.meta.env.DEV
  ? 'http://localhost:5173/'
  : 'https://studio.automaton.monster/'

const defaultApiBaseUrl = import.meta.env.DEV
  ? 'https://localhost:7091/'
  : 'https://api.automaton.monster/'

const defaultPublicAppsBaseUrl = import.meta.env.DEV
  ? defaultApiBaseUrl
  : 'https://apps.automaton.monster/'

const publicSiteUrl = ensureTrailingSlash(import.meta.env.VITE_PUBLIC_SITE_URL || defaultPublicSiteUrl)
const studioUrl = ensureTrailingSlash(import.meta.env.VITE_STUDIO_URL || defaultStudioUrl)
const publicAppsBaseUrl = trimTrailingSlash(import.meta.env.VITE_PUBLIC_APPS_BASE_URL || defaultPublicAppsBaseUrl)
const apiBaseUrl = ensureTrailingSlash(import.meta.env.VITE_API_BASE_URL || defaultApiBaseUrl)

export function getPublishedAppUrl(slug?: string | null): string {
  const normalizedSlug = slug?.trim()
  if (!normalizedSlug) return ''

  const encodedSlug = encodeURIComponent(normalizedSlug)
  const base = new URL(publicAppsBaseUrl, window.location.origin)
  const host = base.hostname.toLowerCase()
  const isLocal = host === 'localhost' || host === '127.0.0.1' || host === '[::1]'
  if (!isLocal && host.startsWith('apps.')) {
    base.hostname = `${encodedSlug}.apps.${host.slice('apps.'.length)}`
    base.pathname = '/'
    base.search = ''
    base.hash = ''
    return base.toString()
  }

  const pathPrefix = shouldNamespacePublishedApps(publicAppsBaseUrl) ? '/apps' : ''
  return `${trimTrailingSlash(publicAppsBaseUrl)}${pathPrefix}/${encodedSlug}/`
}

export function getDeployedAppUrl(appId?: string | null): string {
  const normalizedAppId = appId?.trim()
  if (!normalizedAppId) return ''

  const base = new URL(publicAppsBaseUrl, window.location.origin)
  base.hostname = `app-${normalizedAppId.replace(/-/g, '')}.${base.hostname}`
  base.pathname = '/'
  base.search = ''
  base.hash = ''
  return base.toString()
}

export const publicAppConfig = {
  publicSiteUrl,
  studioUrl,
  publicAppsBaseUrl,
  docsUrl: joinUrl(publicSiteUrl, 'docs'),
  apiBaseUrl,
  llmsUrl: joinUrl(publicSiteUrl, 'llms.txt'),
  cliInstallGuideUrl: joinUrl(publicSiteUrl, 'llms/automaton-cli-install.md'),
  cliWorkflowGuideUrl: joinUrl(publicSiteUrl, 'llms/automaton-cli-deploy.md'),
  appsOverviewUrl: joinUrl(publicSiteUrl, 'llms/automaton-apps-overview.md'),
  frontendStructureUrl: joinUrl(publicSiteUrl, 'llms/automaton-frontend-structure.md'),
  backendControllersUrl: joinUrl(publicSiteUrl, 'llms/automaton-backend-controllers.md'),
  starterPromptsUrl: joinUrl(publicSiteUrl, 'llms/automaton-starter-prompts.md'),
  mcpUrl: joinUrl(apiBaseUrl, 'api/mcp'),
} as const
