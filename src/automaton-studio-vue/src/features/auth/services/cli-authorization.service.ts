import apiClient from '@/shared/services/api/axios-instance'

export async function approveCliAuthorization(requestId: string, userCode: string): Promise<void> {
  await apiClient.post(`/api/cli-auth/requests/${encodeURIComponent(requestId)}/approve`, {
    userCode,
  })
}
