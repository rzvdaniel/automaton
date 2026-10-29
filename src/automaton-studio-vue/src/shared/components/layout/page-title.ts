const PAGE_TITLES: Record<string, string> = {
  dashboard: 'Dashboard',
  'flow-generation': 'New Flow',
  'app-editor': 'App Operations',
  'step-editor': 'Step Editor',
  runners: 'Runners',
  'project-settings': 'Project Settings',
  apikeys: 'API Keys',
  'ai-keys': 'AI Providers',
  'ai-keys-add': 'Add AI Provider',
  'ai-keys-edit': 'Edit AI Provider',
  settings: 'Settings',
  'cli-authorize': 'Authorize CLI',
}

// A route's own meta title wins; unmapped route names become title-case words,
// e.g. 'organization-members' -> 'Organization Members'.
export function pageTitleForRoute(name: string | null | undefined, metaTitle?: unknown): string {
  if (typeof metaTitle === 'string' && metaTitle.trim()) return metaTitle
  if (!name) return 'Dashboard'
  return PAGE_TITLES[name]
    ?? name.split('-').filter(Boolean).map(word => word.charAt(0).toUpperCase() + word.slice(1)).join(' ')
}
