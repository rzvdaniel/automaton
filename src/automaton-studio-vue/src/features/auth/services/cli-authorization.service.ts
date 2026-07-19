import apiClient from '@/shared/services/api/axios-instance'

export async function approveCliAuthorization(requestId: string, userCode: string): Promise<void> {
  await apiClient.post(`/api/cli-auth/requests/${encodeURIComponent(requestId)}/approve`, {
    userCode,
  })
}

export interface CliAuthorizationDetails {
  deviceName: string
  remoteIp?: string | null
  createdAt: string
  expiresAt: string
  status: string
}

export async function getCliAuthorizationDetails(
  requestId: string,
  userCode: string,
): Promise<CliAuthorizationDetails> {
  const { data } = await apiClient.get<CliAuthorizationDetails>(
    `/api/cli-auth/requests/${encodeURIComponent(requestId)}`,
    { params: { code: userCode } },
  )
  return data
}
