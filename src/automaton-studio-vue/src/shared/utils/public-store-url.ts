const publicStoreOrigin =
  import.meta.env.VITE_PUBLIC_STORE_URL ||
  (window.location.hostname === 'localhost' ? 'http://localhost:5174' : 'https://automaton.monster')

export function getPublicStoreUrl(path = ''): string {
  const normalizedPath = path ? `/${path.replace(/^\/+/, '')}` : ''
  return `${publicStoreOrigin.replace(/\/+$/, '')}${normalizedPath}`
}
