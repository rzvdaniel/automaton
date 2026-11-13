export interface RailItem {
  label: string
  icon: string
  to: string
  /** True when the current path belongs to this item. */
  match: (path: string) => boolean
  /** Hidden from people whose only role is AppUser in every organization. */
  hideForAppUser?: boolean
  /** Only for system administrators. */
  sysAdminOnly?: boolean
  /** Shows the waiting-approvals count as a badge. */
  waitingBadge?: boolean
}

const startsWith = (prefix: string) => (path: string) => path === prefix || path.startsWith(`${prefix}/`)

// The six places of Studio.
const MAIN: RailItem[] = [
  { label: 'Apps', icon: 'pi pi-th-large', to: '/apps', match: (p) => startsWith('/apps')(p) && !startsWith('/apps/catalog')(p) },
  { label: 'Catalog', icon: 'pi pi-download', to: '/apps/catalog', match: startsWith('/apps/catalog'), hideForAppUser: true },
  { label: 'People', icon: 'pi pi-users', to: '/people', match: (p) => startsWith('/people')(p) || startsWith('/organizations')(p) || startsWith('/admin/users')(p) },
  { label: 'Server', icon: 'pi pi-server', to: '/server', match: (p) => startsWith('/server')(p) || startsWith('/runners')(p), hideForAppUser: true },
  { label: 'Backups', icon: 'pi pi-database', to: '/admin/backups', match: startsWith('/admin/backups'), sysAdminOnly: true },
  { label: 'Admin', icon: 'pi pi-shield', to: '/admin', match: (p) => startsWith('/admin')(p) && !startsWith('/admin/backups')(p) && !startsWith('/admin/users')(p), sysAdminOnly: true, waitingBadge: true },
  { label: 'Settings', icon: 'pi pi-cog', to: '/settings', match: startsWith('/settings') },
]

// Small link at the bottom of the rail, not a place of its own.
export const DOCS_LINK = { label: 'Documentation', icon: 'pi pi-book', href: 'https://automaton.monster/docs' }

export function railItems(who: { roles: string[]; appUserOnly: boolean }): { main: RailItem[] } {
  const sysAdmin = who.roles.includes('SysAdmin')
  const allowed = (item: RailItem) =>
    !(item.hideForAppUser && who.appUserOnly) && !(item.sysAdminOnly && !sysAdmin)
  return { main: MAIN.filter(allowed) }
}

export function roleLabel(roles: string[], appUserOnly: boolean): string {
  if (roles.includes('SysAdmin')) return 'System administrator'
  if (roles.includes('Admin')) return 'Administrator'
  return appUserOnly ? 'App user' : 'Member'
}
