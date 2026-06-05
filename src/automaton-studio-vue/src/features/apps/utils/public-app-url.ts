export function getPublicAppsBaseUrl(): string {
  return (import.meta.env.VITE_PUBLIC_APPS_BASE_URL || window.location.origin).replace(/\/+$/, '')
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
