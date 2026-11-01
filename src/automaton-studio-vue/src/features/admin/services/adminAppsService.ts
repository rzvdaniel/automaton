import apiClient from '@/shared/services/api/axios-instance'
import type { FileNode } from '@/features/apps/types/apps.types'
import { appsService } from '@/features/apps/services/apps.service'
import type { AdminAppDetails, AdminAppModerationRequest } from '../types/admin.types'

export interface AdminAppPermissionStatus { permission: string; requested: boolean; approved: boolean; status: string }

class AdminAppsService {
  private encodePath(path: string): string {
    return path.split('/').map(encodeURIComponent).join('/')
  }

  async getApp(appId: string): Promise<AdminAppDetails> {
    const { data } = await apiClient.get<AdminAppDetails>(`/api/admin/apps/${appId}`)
    return data
  }

  async getRootFileTree(appId: string): Promise<FileNode[]> {
    const { data } = await apiClient.get<{ success: boolean; files: string[] }>(`/api/admin/apps/${appId}/filesystem/root`)
    return appsService.buildFileTree(data.files ?? [], { includeMetadata: true })
  }

  async readRootFile(appId: string, path: string): Promise<string> {
    const encodedPath = this.encodePath(path)
    const { data } = await apiClient.get<{ success: boolean; content: string }>(`/api/admin/apps/${appId}/filesystem/root/${encodedPath}`)
    return data.content ?? ''
  }

  async unpublish(appId: string): Promise<void> {
    await apiClient.post(`/api/admin/apps/${appId}/unpublish`, {})
  }

  async updateModeration(appId: string, request: AdminAppModerationRequest): Promise<void> {
    await apiClient.put(`/api/admin/apps/${appId}/moderation`, request)
  }

  async getPermissions(appId: string): Promise<AdminAppPermissionStatus[]> {
    const { data } = await apiClient.get<AdminAppPermissionStatus[]>(`/api/admin/apps/${appId}/permissions`)
    return data
  }

  async setPermission(appId: string, permission: string, approved: boolean): Promise<AdminAppPermissionStatus[]> {
    const { data } = await apiClient.put<AdminAppPermissionStatus[]>(
      `/api/admin/apps/${appId}/permissions/${encodeURIComponent(permission)}`, { approved })
    return data
  }
}

export const adminAppsService = new AdminAppsService()
