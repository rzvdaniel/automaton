export function getPublicAppsBaseUrl(): string {
  const configuredBaseUrl = import.meta.env.VITE_PUBLIC_APPS_BASE_URL?.trim()
  if (configuredBaseUrl) {
    return configuredBaseUrl.replace(/\/+$/, '')
  }

  const apiBaseUrl = import.meta.env.VITE_API_BASE_URL?.trim()
  if (apiBaseUrl) {
    return new URL(apiBaseUrl, window.location.origin).origin.replace(/\/+$/, '')
  }

  return window.location.origin.replace(/\/+$/, '')
}

function shouldNamespacePublishedApps(baseUrl: string): boolean {
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

export function getPublishedAppsUrlPrefix(): string {
  const baseUrl = getPublicAppsBaseUrl()
  return shouldNamespacePublishedApps(baseUrl) ? `${baseUrl}/apps/` : `${baseUrl}/`
}

export function getDeployedAppUrl(appId?: string | null): string {
  const normalizedAppId = appId?.trim()
  return normalizedAppId ? `${getPublicAppsBaseUrl()}/a/${encodeURIComponent(normalizedAppId)}/` : ''
}

export function getPublishedAppUrl(slug?: string | null): string {
  const normalizedSlug = slug?.trim()
  if (!normalizedSlug) return ''

  const baseUrl = getPublicAppsBaseUrl()
  const pathPrefix = shouldNamespacePublishedApps(baseUrl) ? '/apps' : ''
  return `${baseUrl}${pathPrefix}/${encodeURIComponent(normalizedSlug)}/`
}

export function getOpenAppUrl(app: { id?: string | null; isPublished?: boolean; publicUrl?: string | null }): string {
  return getDeployedAppUrl(app.id)
}
