import type { RouteLocationRaw } from 'vue-router'
import { APP_VIEW_KEYS } from '@/features/apps/app-navigation'
import { pageTitleForRoute } from './page-title'

export interface Crumb {
  label: string
  /** Absent on the last crumb: the page you are on. */
  to?: RouteLocationRaw
}

const ADMIN_PAGES = new Set(['admin-permission-requests', 'admin-apps', 'admin-logs', 'admin-app-deploy-rules', 'admin-app-activity'])
const ORGANIZATION_PAGES = new Set(['organization-dashboard', 'organization-members', 'organization-apps', 'organization-settings'])

/**
 * The trail shown in the top bar for pages below a top-level page, or [] for a top-level page
 * (those show their name once, as the page heading).
 */
export function crumbsForRoute(
  name: string | null | undefined,
  options: { params?: Record<string, unknown>; query?: Record<string, unknown>; meta?: unknown; appName?: string } = {},
): Crumb[] {
  if (!name) return []
  const title = pageTitleForRoute(name, options.meta)
  if (name === 'app-editor') {
    const id = String(options.params?.id ?? '')
    const trail: Crumb[] = [{ label: 'Apps', to: '/apps' }]
    const view = String(options.query?.view ?? '')
    if (view !== 'overview' && (APP_VIEW_KEYS as readonly string[]).includes(view)) {
      const label = view === 'store' ? 'Store listing' : view.charAt(0).toUpperCase() + view.slice(1)
      trail.push({ label: options.appName || 'App', to: `/apps/${id}` }, { label })
    } else {
      trail.push({ label: options.appName || 'App' })
    }
    return trail
  }
  if (ADMIN_PAGES.has(name)) return [{ label: 'Admin', to: '/admin/apps' }, { label: title }]
  if (name === 'admin-user-detail') return [{ label: 'People', to: '/people' }, { label: 'User' }]
  if (name === 'admin-user-apps') {
    return [{ label: 'People', to: '/people' }, { label: 'User', to: `/admin/users/${String(options.params?.userId ?? '')}` }, { label: 'Apps' }]
  }
  if (ORGANIZATION_PAGES.has(name)) return [{ label: 'People', to: '/people' }, { label: title }]
  return []
}
