const PAGE_TITLES: Record<string, string> = {
  'app-editor': 'App Operations',
  runners: 'Server',
  apikeys: 'Settings',
  settings: 'Settings',
  'cli-authorize': 'Authorize CLI',
}

// A route's own meta title wins; unmapped route names become title-case words,
// e.g. 'organization-members' -> 'Organization Members'.
export function pageTitleForRoute(name: string | null | undefined, metaTitle?: unknown): string {
  if (typeof metaTitle === 'string' && metaTitle.trim()) return metaTitle
  if (!name) return 'Apps'
  return PAGE_TITLES[name]
    ?? name.split('-').filter(Boolean).map(word => word.charAt(0).toUpperCase() + word.slice(1)).join(' ')
}
