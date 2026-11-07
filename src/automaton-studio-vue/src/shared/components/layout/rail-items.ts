export interface RailItem {
  label: string
  icon: string
  to: string
  external?: boolean
  /** True when the current path belongs to this item. */
  match: (path: string) => boolean
  /** Hidden from people whose only role is AppUser in every organization. */
  hideForAppUser?: boolean
  /** Only for system administrators. */
  sysAdminOnly?: boolean
  /** Only for administrators (Admin or SysAdmin). */
  adminOnly?: boolean
}

const startsWith = (prefix: string) => (path: string) => path === prefix || path.startsWith(`${prefix}/`)

// The six places of Studio. People, Server and Backups point at the pages that exist today until their redesign.
const MAIN: RailItem[] = [
  { label: 'Apps', icon: 'pi pi-th-large', to: '/apps', match: (p) => startsWith('/apps')(p) && !startsWith('/apps/catalog')(p) },
  { label: 'Catalog', icon: 'pi pi-download', to: '/apps/catalog', match: startsWith('/apps/catalog'), hideForAppUser: true },
  { label: 'People', icon: 'pi pi-users', to: '/organizations', match: startsWith('/organizations') },
  { label: 'Server', icon: 'pi pi-server', to: '/runners', match: startsWith('/runners'), hideForAppUser: true },
  { label: 'Backups', icon: 'pi pi-database', to: '/admin/installation', match: startsWith('/admin/installation'), sysAdminOnly: true },
  { label: 'Settings', icon: 'pi pi-cog', to: '/settings', match: startsWith('/settings') },
]

// Pages not yet moved into the six places stay reachable from here.
const MORE: RailItem[] = [
  { label: 'API Keys', icon: 'pi pi-key', to: '/apikeys', match: startsWith('/apikeys'), hideForAppUser: true },
  { label: 'CLI', icon: 'pi pi-terminal', to: '/downloads', match: startsWith('/downloads'), hideForAppUser: true },
  { label: 'Admin', icon: 'pi pi-shield', to: '/admin', match: (p) => startsWith('/admin')(p) && !startsWith('/admin/installation')(p), adminOnly: true },
  { label: 'Documentation', icon: 'pi pi-book', to: 'https://automaton.monster/docs', external: true, match: () => false },
]

export function railItems(who: { roles: string[]; appUserOnly: boolean }): { main: RailItem[]; more: RailItem[] } {
  const sysAdmin = who.roles.includes('SysAdmin')
  const admin = sysAdmin || who.roles.includes('Admin')
  const allowed = (item: RailItem) =>
    !(item.hideForAppUser && who.appUserOnly) && !(item.sysAdminOnly && !sysAdmin) && !(item.adminOnly && !admin)
  return { main: MAIN.filter(allowed), more: MORE.filter(allowed) }
}

export function roleLabel(roles: string[], appUserOnly: boolean): string {
  if (roles.includes('SysAdmin')) return 'System administrator'
  if (roles.includes('Admin')) return 'Administrator'
  return appUserOnly ? 'App user' : 'Member'
}
