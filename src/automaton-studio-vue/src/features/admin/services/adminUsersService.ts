import apiClient from '@/shared/services/api/axios-instance'
import type { AdminUser } from '../types/admin.types'

interface AdminUsersQuery {
  name?: string
  email?: string
  pageIndex?: number
  pageSize?: number
}

class AdminUsersService {
  async getUsers(query: AdminUsersQuery = {}): Promise<AdminUser[]> {
    const { data } = await apiClient.get<AdminUser[]>('/api/admin/users', {
      params: {
        name: query.name,
        email: query.email,
        pageIndex: query.pageIndex ?? 0,
        pageSize: query.pageSize ?? 100,
      },
    })

    return data
  }
}

export const adminUsersService = new AdminUsersService()