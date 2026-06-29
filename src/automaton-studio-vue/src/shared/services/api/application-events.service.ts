import axiosInstance from './axios-instance'
import type { ApplicationEventFilter, ApplicationEventPage } from '@/shared/types/ai.types'

export const applicationEventsService = {
  async getEvents(appId: string, filter?: ApplicationEventFilter): Promise<ApplicationEventPage> {
    const params: Record<string, any> = {}
    if (filter?.category) params.category = filter.category
    if (filter?.level) params.level = filter.level
    if (filter?.from) params.from = filter.from
    if (filter?.to) params.to = filter.to
    if (filter?.page) params.page = filter.page
    if (filter?.pageSize) params.pageSize = filter.pageSize

    const response = await axiosInstance.get<ApplicationEventPage>(
      `/api/apps/${appId}/application-events`,
      { params },
    )
    return response.data
  },
}
