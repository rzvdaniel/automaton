import apiClient from '@/shared/services/api/axios-instance'
import type { NotificationItem } from '../types/home.types'

export const notificationsService = {
  async list(): Promise<NotificationItem[]> {
    const { data } = await apiClient.get<{ notifications: NotificationItem[] }>('/api/notifications')
    return data.notifications
  },
  async acknowledge(key: string): Promise<void> {
    await apiClient.put(`/api/notifications/${encodeURIComponent(key)}/acknowledgement`)
  },
}
