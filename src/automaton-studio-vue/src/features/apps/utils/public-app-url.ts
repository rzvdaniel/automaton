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
  if (!normalizedAppId) return ''

  const baseUrl = new URL(getPublicAppsBaseUrl(), window.location.origin)
  const appsMarker = baseUrl.hostname.indexOf('.apps.')
  baseUrl.hostname = appsMarker >= 0
    ? `app-${normalizedAppId.replace(/-/g, '')}${baseUrl.hostname.slice(appsMarker)}`
    : `app-${normalizedAppId.replace(/-/g, '')}.${baseUrl.hostname}`
  baseUrl.pathname = '/'
  return baseUrl.toString()
}

export function getDeploymentTargetUrl(appId?: string | null, routePath?: string | null): string {
  const deployedAppUrl = getDeployedAppUrl(appId)
  const normalizedRoutePath = routePath?.trim()
  if (!deployedAppUrl || !normalizedRoutePath) return ''

  return new URL(normalizedRoutePath, deployedAppUrl).toString()
}

export function getStudioAppUrl(appId?: string | null): string {
  const normalizedAppId = appId?.trim()
  if (!normalizedAppId) return ''

  const sameOriginStudioUrl = `${window.location.origin.replace(/\/+$/, '')}/apps/${encodeURIComponent(normalizedAppId)}`

  const apiBaseUrl = import.meta.env.VITE_API_BASE_URL?.trim()
  if (apiBaseUrl) {
    try {
      const parsed = new URL(apiBaseUrl, window.location.origin)
      if (parsed.hostname.startsWith('api.')) {
        parsed.hostname = `studio.${parsed.hostname.slice('api.'.length)}`
        parsed.pathname = `/apps/${encodeURIComponent(normalizedAppId)}`
        parsed.search = ''
        parsed.hash = ''
        return parsed.toString().replace(/\/+$/, '')
      }

      if (parsed.hostname === 'localhost' || parsed.hostname === '127.0.0.1') {
        return sameOriginStudioUrl
      }
    } catch {
      // fall through to same-origin studio path
    }
  }

  return sameOriginStudioUrl
}

export function getStudioApplicationUrl(appId?: string | null): string {
  const studioAppUrl = getStudioAppUrl(appId)
  return studioAppUrl ? `${studioAppUrl}/application` : ''
}

export function getPublishedAppUrl(slug?: string | null): string {
  const normalizedSlug = slug?.trim()
  if (!normalizedSlug) return ''

  const baseUrl = new URL(getPublicAppsBaseUrl(), window.location.origin)
  const appsMarker = baseUrl.hostname.indexOf('.apps.')
  const rootDomain = appsMarker >= 0
    ? baseUrl.hostname.slice(appsMarker + '.apps.'.length)
    : baseUrl.hostname.replace(/^apps\./, '')
  baseUrl.hostname = `${normalizedSlug}.apps.${rootDomain}`
  baseUrl.pathname = '/'
  return baseUrl.toString()
}

export function getOpenAppUrl(app: { id?: string | null; isPublished?: boolean; publicUrl?: string | null }): string {
  return app.isPublished && app.publicUrl
    ? getPublishedAppUrl(app.publicUrl)
    : getDeployedAppUrl(app.id)
}
