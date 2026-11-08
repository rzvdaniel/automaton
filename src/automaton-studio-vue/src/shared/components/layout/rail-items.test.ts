import { describe, expect, it } from 'vitest'
import { railItems, roleLabel } from './rail-items'

const labels = (items: { label: string }[]) => items.map((item) => item.label)

describe('railItems', () => {
  it('shows the six places to a system administrator, plus Admin under More', () => {
    const { main, more } = railItems({ roles: ['SysAdmin'], appUserOnly: false })
    expect(labels(main)).toEqual(['Apps', 'Catalog', 'People', 'Server', 'Backups', 'Settings'])
    expect(labels(more)).toContain('Admin')
  })

  it('hides Backups and Admin from members', () => {
    const { main, more } = railItems({ roles: ['User'], appUserOnly: false })
    expect(labels(main)).toEqual(['Apps', 'Catalog', 'People', 'Server', 'Settings'])
    expect(labels(more)).not.toContain('Admin')
  })

  it('leaves app-only members with Apps, People and Settings', () => {
    const { main, more } = railItems({ roles: ['User'], appUserOnly: true })
    expect(labels(main)).toEqual(['Apps', 'People', 'Settings'])
    expect(labels(more)).toEqual(['Documentation'])
  })

  it('points each place at the page that exists today', () => {
    const { main } = railItems({ roles: ['SysAdmin'], appUserOnly: false })
    expect(Object.fromEntries(main.map((item) => [item.label, item.to]))).toEqual({
      Apps: '/apps',
      Catalog: '/apps/catalog',
      People: '/people',
      Server: '/server',
      Backups: '/admin/backups',
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
