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

  async hardDeleteUser(userId: string, confirmationText: string): Promise<void> {
    await apiClient.delete(`/api/admin/users/${userId}/hard`, {
      data: { confirmationText },
    })
  }
}

export const adminUsersService = new AdminUsersService()
