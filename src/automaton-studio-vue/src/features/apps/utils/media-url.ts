export function resolveMediaUrl(url?: string | null): string {
  if (!url) return ''
  if (/^(https?:)?\/\//i.test(url)) return url

  const baseUrl = (import.meta.env.VITE_API_BASE_URL || '').replace(/\/+$/, '')
  const relativeUrl = url.replace(/^\/+/, '')
  return baseUrl ? `${baseUrl}/${relativeUrl}` : `/${relativeUrl}`
}
