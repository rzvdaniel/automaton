export function getPublicAppsBaseUrl(): string {
  return (import.meta.env.VITE_PUBLIC_APPS_BASE_URL || window.location.origin).replace(/\/+$/, '')
}

export function getPublishedAppUrl(slug?: string | null): string {
  const normalizedSlug = slug?.trim()
  return normalizedSlug ? `${getPublicAppsBaseUrl()}/${encodeURIComponent(normalizedSlug)}/` : ''
}
