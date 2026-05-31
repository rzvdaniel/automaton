import apiClient from '@/shared/services/api/axios-instance'
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
}

export const adminUsersService = new AdminUsersService()