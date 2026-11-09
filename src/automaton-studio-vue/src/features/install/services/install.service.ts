import apiClient from '@/shared/services/api/axios-instance'
import type { InstallCommand, InstallationStatusResponse, InstallationCheck, InstallationBackupStatus, InstallationUpdateStatus, InstallationSummary } from '../types/install.types'

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

  /** Counts, version and whether email is configured (system administrators). */
  async getSummary(): Promise<InstallationSummary> {
    const { data } = await apiClient.get<InstallationSummary>('/api/install/summary')
    return data
  }

  /** Asks the host to take a backup now (system administrators). */
  async startBackup(): Promise<void> {
    await apiClient.post('/api/install/backups')
  }

  async getBackups(): Promise<InstallationBackupStatus> {
    const { data } = await apiClient.get<InstallationBackupStatus>('/api/install/backups')
    return data
  }

  async getUpdate(): Promise<InstallationUpdateStatus> {
    const { data } = await apiClient.get<InstallationUpdateStatus>('/api/install/update')
    return data
  }

  async startUpdate(version = 'latest'): Promise<void> {
    await apiClient.post('/api/install/update', { version })
  }

  async install(command: InstallCommand, setupToken: string): Promise<void> {
    await apiClient.post('/api/install', command, {
      headers: { 'X-Automaton-Setup-Token': setupToken },
    })
  }
}
export const installService = new InstallService()
