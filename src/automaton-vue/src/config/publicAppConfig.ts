function trimTrailingSlash(value: string) {
  return value.replace(/\/+$/, '')
}

function ensureTrailingSlash(value: string) {
  return `${trimTrailingSlash(value)}/`
}

function joinUrl(base: string, path: string) {
  return `${trimTrailingSlash(base)}/${path.replace(/^\/+/, '')}`
}

const defaultPublicSiteUrl = import.meta.env.DEV
  ? 'http://localhost:5174/'
  : 'https://automaton.monster/'

const defaultStudioUrl = import.meta.env.DEV
  ? 'http://localhost:5173/'
  : 'https://studio.automaton.monster/'

const defaultApiBaseUrl = import.meta.env.DEV
  ? 'https://localhost:7091/'
  : 'https://api.automaton.monster/'

const publicSiteUrl = ensureTrailingSlash(import.meta.env.VITE_PUBLIC_SITE_URL || defaultPublicSiteUrl)
const studioUrl = ensureTrailingSlash(import.meta.env.VITE_STUDIO_URL || defaultStudioUrl)
const apiBaseUrl = ensureTrailingSlash(import.meta.env.VITE_API_BASE_URL || defaultApiBaseUrl)
export const publicAppConfig = {
  publicSiteUrl,
  studioUrl,
  docsUrl: joinUrl(publicSiteUrl, 'docs'),
  apiBaseUrl,
  llmsUrl: joinUrl(publicSiteUrl, 'llms.txt'),
  cliInstallGuideUrl: joinUrl(publicSiteUrl, 'llms/automaton-cli-install.md'),
  cliWorkflowGuideUrl: joinUrl(publicSiteUrl, 'llms/automaton-cli-deploy.md'),
  appsOverviewUrl: joinUrl(publicSiteUrl, 'llms/automaton-apps-overview.md'),
  frontendStructureUrl: joinUrl(publicSiteUrl, 'llms/automaton-frontend-structure.md'),
  backendControllersUrl: joinUrl(publicSiteUrl, 'llms/automaton-backend-controllers.md'),
  starterPromptsUrl: joinUrl(publicSiteUrl, 'llms/automaton-starter-prompts.md'),
  mcpUrl: joinUrl(apiBaseUrl, 'api/mcp'),
} as const
