import apiClient from '@/shared/services/api/axios-instance'
import type { App } from '@/features/apps/types/apps.types'
import type { AdminUser, AdminUserPage } from '../types/admin.types'

interface AdminUsersQuery {
  search?: string
  name?: string
  email?: string
  registrationProvider?: string
  emailConfirmed?: boolean
  isLocked?: boolean
  registeredFromUtc?: string
  registeredToUtc?: string
  sortField?: string
  sortOrder?: number
  pageIndex?: number
  pageSize?: number
}

export interface AdminInviteUserRequest { email: string; name?: string; administrator?: boolean }
export interface AdminCreateUserRequest { email: string; name?: string; password?: string; administrator?: boolean }
export interface AdminProvisionedUser {
  userId: string
  email: string
  name: string
  invitationSent: boolean
  resent: boolean
  generatedPassword?: string | null
  warning?: string | null
}

class AdminUsersService {
  async getUsers(query: AdminUsersQuery = {}): Promise<AdminUserPage> {
    const { data } = await apiClient.get<AdminUserPage>('/api/admin/users', {
      params: {
        search: query.search,
        name: query.name,
        email: query.email,
        registrationProvider: query.registrationProvider,
        emailConfirmed: query.emailConfirmed,
        isLocked: query.isLocked,
        registeredFromUtc: query.registeredFromUtc,
        registeredToUtc: query.registeredToUtc,
        sortField: query.sortField,
        sortOrder: query.sortOrder,
        pageIndex: query.pageIndex ?? 0,
        pageSize: query.pageSize ?? 100,
      },
    })

    return data
  }

  async getUser(userId: string): Promise<AdminUser> {
    const { data } = await apiClient.get<AdminUser>(`/api/admin/users/${userId}`)
    return data
  }

  async getUserApps(userId: string, projectId?: string): Promise<App[]> {
    const { data } = await apiClient.get<App[]>(`/api/admin/users/${userId}/apps`, {
      params: projectId ? { projectId } : undefined,
    })
    return data
  }

  async disableUser(userId: string): Promise<void> {
    await apiClient.post(`/api/admin/users/${userId}/disable`)
  }

  async enableUser(userId: string): Promise<void> {
    await apiClient.post(`/api/admin/users/${userId}/enable`)
  }

  async resendConfirmation(userId: string): Promise<void> {
    await apiClient.post(`/api/admin/users/${userId}/resend-confirmation`)
  }

  /** Creates the account without a password and emails a one-time link to set it (resends for pending accounts). */
  async inviteUser(request: AdminInviteUserRequest): Promise<AdminProvisionedUser> {
    const { data } = await apiClient.post<AdminProvisionedUser>('/api/admin/users/invite', request)
    return data
  }

  /** Creates the account with a password; a generated password is returned once. No email is sent. */
  async createUser(request: AdminCreateUserRequest): Promise<AdminProvisionedUser> {
    const { data } = await apiClient.post<AdminProvisionedUser>('/api/admin/users', request)
    return data
  }

  async hardDeleteUser(userId: string, confirmationText: string): Promise<void> {
    await apiClient.delete(`/api/admin/users/${userId}/hard`, {
      data: { confirmationText },
    })
  }
}

export const adminUsersService = new AdminUsersService()
