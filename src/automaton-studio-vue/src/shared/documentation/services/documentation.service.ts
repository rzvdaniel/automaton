import apiClient from '@/shared/services/api/axios-instance'

export interface AdminDocumentationTopicListItem {
  key: string
  title: string
  category: string
  group: string
  sortOrder: number
}

export interface AdminDocumentationTopic {
  key: string
  title: string
  category: string
  group: string
  sortOrder: number
  contentMarkdown: string
  updatedAtUtc: string
}

class DocumentationService {
  async listAdminTopics(): Promise<AdminDocumentationTopicListItem[]> {
    const { data } = await apiClient.get<AdminDocumentationTopicListItem[]>('/api/admin/documentation')
    return data
  }

  async getAdminTopic(key: string): Promise<AdminDocumentationTopic> {
    const { data } = await apiClient.get<AdminDocumentationTopic>(`/api/admin/documentation/${encodeURIComponent(key)}`)
    return data
  }

  async getAdminAsset(url: string): Promise<Blob> {
    const { data } = await apiClient.get<Blob>(url, { responseType: 'blob' })
    return data
  }
}

export const documentationService = new DocumentationService()
