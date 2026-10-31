import apiClient from '@/shared/services/api/axios-instance'
import type { InstallCommand, InstallationStatusResponse, InstallationCheck, InstallationBackupStatus } from '../types/install.types'

class InstallService {
  async getStatus(): Promise<InstallationStatusResponse> {
    const { data } = await apiClient.get<InstallationStatusResponse>('/api/install/status')
    return data
  }

  async check(setupToken = ''): Promise<InstallationCheck[]> {
    const { data } = await apiClient.post<InstallationCheck[]>('/api/install/checks', null, {
      headers: { 'X-Automaton-Setup-Token': setupToken },
    })
    return data
  }

  async getBackups(): Promise<InstallationBackupStatus> {
    const { data } = await apiClient.get<InstallationBackupStatus>('/api/install/backups')
    return data
  }

  async install(command: InstallCommand, setupToken: string): Promise<void> {
    await apiClient.post('/api/install', command, {
      headers: { 'X-Automaton-Setup-Token': setupToken },
    })
  }
}
export const installService = new InstallService()
