import apiClient from '@/shared/services/api/axios-instance'
import type { SmtpSettingsSummary, SmtpSettingsUpdate } from '../types/install.types'

const path = '/api/admin/installation/smtp'

export const smtpSettingsService = {
  async get(): Promise<SmtpSettingsSummary> {
    const { data } = await apiClient.get<SmtpSettingsSummary>(path)
    return data
  },
  async save(settings: SmtpSettingsUpdate): Promise<void> {
    await apiClient.put(path, settings)
  },
  async verify(): Promise<void> {
    await apiClient.post(`${path}/verify`)
  },
  async sendTest(recipient: string): Promise<void> {
    await apiClient.post(`${path}/test-email`, { recipient })
  },
}
