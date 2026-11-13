import { describe, expect, it } from 'vitest'
import { railItems, roleLabel } from './rail-items'

const labels = (items: { label: string }[]) => items.map((item) => item.label)

describe('railItems', () => {
  it('shows exactly the six places to a system administrator', () => {
    const { main } = railItems({ roles: ['SysAdmin'], appUserOnly: false })
    expect(labels(main)).toEqual(['Apps', 'Catalog', 'People', 'Server', 'Backups', 'Admin', 'Settings'])
  })

  it('keeps Backups and People for their admin pages and Admin for the rest', () => {
    const { main } = railItems({ roles: ['SysAdmin'], appUserOnly: false })
    const active = (path: string) => labels(main.filter((item) => item.match(path)))
    expect(active('/admin')).toEqual(['Admin'])
    expect(active('/admin/apps/x')).toEqual(['Admin'])
    expect(active('/admin/backups')).toEqual(['Backups'])
    expect(active('/admin/users')).toEqual(['People'])
  })

  it('shows Admin, but not Backups, to a plain administrator', () => {
    const { main } = railItems({ roles: ['Admin'], appUserOnly: false })
    expect(labels(main)).toEqual(['Apps', 'Catalog', 'People', 'Server', 'Admin', 'Settings'])
  })

  it('hides Backups from members', () => {
    const { main } = railItems({ roles: ['User'], appUserOnly: false })
    expect(labels(main)).toEqual(['Apps', 'Catalog', 'People', 'Server', 'Settings'])
  })

  it('leaves app-only members with Apps, People and Settings', () => {
    const { main } = railItems({ roles: ['User'], appUserOnly: true })
    expect(labels(main)).toEqual(['Apps', 'People', 'Settings'])
  })

  it('points each place at the page that exists today', () => {
    const { main } = railItems({ roles: ['SysAdmin'], appUserOnly: false })
    expect(Object.fromEntries(main.map((item) => [item.label, item.to]))).toEqual({
      Apps: '/apps',
      Catalog: '/apps/catalog',
      People: '/people',
      Server: '/server',
      Backups: '/admin/backups',
      Admin: '/admin',
      Settings: '/settings',
    })
  })

  it('marks Catalog, not Apps, active on the catalog path', () => {
    const { main } = railItems({ roles: ['SysAdmin'], appUserOnly: false })
    const active = (path: string) => labels(main.filter((item) => item.match(path)))
    expect(active('/apps/catalog')).toEqual(['Catalog'])
    expect(active('/apps/abc/application')).toEqual(['Apps'])
    expect(active('/organizations/x')).toEqual(['People'])
  })
})

describe('roleLabel', () => {
  it('names the highest role', () => {
    expect(roleLabel(['SysAdmin', 'Admin'], false)).toBe('System administrator')
    expect(roleLabel(['Admin'], false)).toBe('Administrator')
    expect(roleLabel(['User'], true)).toBe('App user')
    expect(roleLabel(['User'], false)).toBe('Member')
  })
})
