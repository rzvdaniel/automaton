import apiClient from '@/shared/services/api/axios-instance'

class ServerConsoleService {
  /** POST /api/server/console/install. System administrators only: installs Server Console for the caller, approves its permissions and deploys it. */
  async install(): Promise<{ appId: string | null }> {
    const { data } = await apiClient.post<{ appId: string | null }>('/api/server/console/install')
    return data
  }
}

export const serverConsoleService = new ServerConsoleService()
