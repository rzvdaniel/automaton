// The one menu of automaton.monster and the App Store. Both sites import this list, so they cannot drift apart.
// `path` is a page of the website (a router link there, an absolute link on the store); `href` is a full URL.

export type SiteNavKey = 'product' | 'use-cases' | 'catalog' | 'live-apps' | 'docs' | 'github'

export interface SiteNavItem {
  key: SiteNavKey
  label: string
  path?: string
  href?: string
  external?: boolean
}

export interface SiteNavUrls {
  storeUrl: string
  docsUrl: string
  githubUrl: string
}

export function buildSiteNav({ storeUrl, docsUrl, githubUrl }: SiteNavUrls): SiteNavItem[] {
  return [
    { key: 'product', label: 'Product', path: '/product' },
    { key: 'use-cases', label: 'Use cases', path: '/use-cases' },
    { key: 'catalog', label: 'Catalog', path: '/catalog' },
    { key: 'live-apps', label: 'Live apps', href: storeUrl },
    { key: 'docs', label: 'Docs', href: docsUrl },
    { key: 'github', label: 'GitHub', href: githubUrl, external: true },
  ]
}
