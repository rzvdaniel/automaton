import { describe, expect, it } from 'vitest'
import type { AdminUser } from '@/features/admin/types/admin.types'
import type { OrganizationMemberDto } from '@/features/organizations/types'
import { adminRoleLabel, isPendingInvite, memberRows, teamsByUser } from './people'

const user = (over: Partial<AdminUser>): AdminUser => ({
  id: 'u1', name: 'Ann', email: 'ann@example.test', emailConfirmed: true, isLocked: false, appCount: 2,
  publishedAppCount: 0, privateAppCount: 2, runnerCount: 0, apps: [], roles: ['User'], ...over,
})
const member = (userId: string, role: OrganizationMemberDto['role'] = 'AppUser'): OrganizationMemberDto => ({
  id: `m-${userId}`, organizationId: 'o1', userId, userName: userId, email: `${userId}@example.test`, role,
  joinedAt: '', isActive: true, isOwner: false,
})

describe('people', () => {
  it('treats an account with no confirmed email and no sign-in as a pending invite', () => {
    expect(isPendingInvite(user({ emailConfirmed: false }))).toBe(true)
    expect(isPendingInvite(user({ emailConfirmed: false, lastLoginAtUtc: '2026-01-01' }))).toBe(false)
    expect(isPendingInvite(user({}))).toBe(false)
  })

  it('labels administrators', () => {
    expect(adminRoleLabel(['Admin'])).toBe('Administrator')
    expect(adminRoleLabel(['SysAdmin', 'Admin'])).toBe('System administrator')
    expect(adminRoleLabel(['User'])).toBe('Member')
  })

  it('lists every confirmed account for administrators, with teams and app counts', () => {
    const teams = [{ name: 'Design', members: [member('u1')] }]
    const rows = memberRows([user({}), user({ id: 'u2', emailConfirmed: false })], teams)
    expect(rows).toHaveLength(1)
    expect(rows[0]).toMatchObject({ id: 'u1', teams: ['Design'], appCount: 2, role: 'Member' })
  })

  it('lists team mates once for everyone else', () => {
    const teams = [
      { name: 'A', members: [member('u1', 'OrgAdmin'), member('u2')] },
      { name: 'B', members: [member('u1', 'OrgAdmin')] },
    ]
    const rows = memberRows(null, teams)
    expect(rows.map((r) => r.id)).toEqual(['u1', 'u2'])
    expect(rows[0]).toMatchObject({ role: 'Team admin', teams: ['A', 'B'], appCount: null })
    expect(teamsByUser(teams).get('u2')).toEqual(['A'])
  })
})
