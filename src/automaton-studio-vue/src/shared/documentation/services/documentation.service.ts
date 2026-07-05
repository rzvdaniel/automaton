import apiClient from '@/shared/services/api/axios-instance'

export interface AdminDocumentationTopic {
  key: string
  title: string
  contentMarkdown: string
  updatedAtUtc: string
}

class DocumentationService {
  async getAdminTopic(key: string): Promise<AdminDocumentationTopic> {
    const { data } = await apiClient.get<AdminDocumentationTopic>(`/api/admin/documentation/${encodeURIComponent(key)}`)
    return data
  }
}

export const documentationService = new DocumentationService()
