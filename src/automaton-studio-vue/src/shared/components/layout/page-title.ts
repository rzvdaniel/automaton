const PAGE_TITLES: Record<string, string> = {
  dashboard: 'Dashboard',
  'flow-list': 'Flows',
  'flow-generation': 'New Flow',
  'app-list': 'Apps',
  'app-editor': 'App Operations',
  'step-list': 'Steps',
  'step-editor': 'Step Editor',
  runners: 'Runners',
  'projects-list': 'Projects',
  'project-settings': 'Project Settings',
  apikeys: 'API Keys',
  'ai-keys': 'AI Providers',
  settings: 'Settings',
  'admin-providers': 'Admin',
  'admin-models': 'Admin',
  'admin-provider-detail': 'Admin',
  'admin-model-detail': 'Admin',
  'cli-authorize': 'Authorize CLI',
}

// Unmapped route names become title-case words, e.g. 'organization-members' -> 'Organization Members'.
export function pageTitleForRoute(name: string | null | undefined): string {
  if (!name) return 'Dashboard'
  return PAGE_TITLES[name]
    ?? name.split('-').filter(Boolean).map(word => word.charAt(0).toUpperCase() + word.slice(1)).join(' ')
}
