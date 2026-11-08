import type { AdminUser } from '@/features/admin/types/admin.types'
import type { OrganizationMemberDto, OrganizationRoleType } from '@/features/organizations/types'

export interface PersonRow {
  id: string
  name: string
  email: string
  role: string
  teams: string[]
  /** Null when the viewer is not an administrator and the count is not available. */
  appCount: number | null
}

export interface TeamWithMembers {
  name: string
  members: OrganizationMemberDto[]
}

/** An invited account that has not set a password yet. */
export function isPendingInvite(user: Pick<AdminUser, 'emailConfirmed' | 'lastLoginAtUtc'>): boolean {
  return !user.emailConfirmed && !user.lastLoginAtUtc
}

export function adminRoleLabel(roles: string[]): string {
  if (roles.includes('SysAdmin')) return 'System administrator'
  if (roles.includes('Admin')) return 'Administrator'
  return 'Member'
}

export function teamRoleLabel(role: OrganizationRoleType): string {
  return role === 'OrgAdmin' ? 'Team admin' : role === 'Developer' ? 'Developer' : 'App user'
}

/** Names of the teams each person belongs to, keyed by user id. */
export function teamsByUser(teams: TeamWithMembers[]): Map<string, string[]> {
  const result = new Map<string, string[]>()
  for (const team of teams) {
    for (const member of team.members) {
      result.set(member.userId, [...(result.get(member.userId) ?? []), team.name])
    }
  }
  return result
}

/** Administrators see every account; everyone else sees the people in the teams they belong to. */
export function memberRows(users: AdminUser[] | null, teams: TeamWithMembers[]): PersonRow[] {
  const teamNames = teamsByUser(teams)
  if (users) {
    return users
      .filter((user) => !isPendingInvite(user))
      .map((user) => ({
        id: user.id,
        name: user.name || user.email,
        email: user.email,
        role: adminRoleLabel(user.roles ?? []),
        teams: teamNames.get(user.id) ?? [],
        appCount: user.appCount,
      }))
  }
  const seen = new Map<string, PersonRow>()
  for (const team of teams) {
    for (const member of team.members) {
      if (seen.has(member.userId)) continue
      seen.set(member.userId, {
        id: member.userId,
        name: member.userName || member.email,
        email: member.email,
        role: teamRoleLabel(member.role),
        teams: teamNames.get(member.userId) ?? [],
        appCount: null,
      })
    }
  }
  return [...seen.values()]
}
