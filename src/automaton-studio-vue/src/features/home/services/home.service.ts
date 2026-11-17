import apiClient from '@/shared/services/api/axios-instance'
import type { HomeData } from '../types/home.types'

export const homeService = {
  async get(): Promise<HomeData> {
    const { data } = await apiClient.get<HomeData>('/api/home')
    return data
  },
  async pin(appId: string): Promise<void> {
    await apiClient.put(`/api/home/pins/${appId}`)
  },
  async unpin(appId: string): Promise<void> {
    await apiClient.delete(`/api/home/pins/${appId}`)
  },
  async reorder(appIds: string[]): Promise<void> {
    await apiClient.put('/api/home/pins/order', { appIds })
  },
  async dismiss(cardKey: string): Promise<void> {
    await apiClient.put(`/api/home/dismissals/${encodeURIComponent(cardKey)}`)
  },
}
