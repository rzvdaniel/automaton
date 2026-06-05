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

export function getDeployedAppUrl(appId?: string | null): string {
  const normalizedAppId = appId?.trim()
  return normalizedAppId ? `${getPublicAppsBaseUrl()}/private/${encodeURIComponent(normalizedAppId)}/` : ''
}

export function getPublishedAppUrl(slug?: string | null): string {
  const normalizedSlug = slug?.trim()
  return normalizedSlug ? `${getPublicAppsBaseUrl()}/${encodeURIComponent(normalizedSlug)}/` : ''
}

export function getOpenAppUrl(app: { id?: string | null; isPublished?: boolean; publicUrl?: string | null }): string {
  return getDeployedAppUrl(app.id)
}
